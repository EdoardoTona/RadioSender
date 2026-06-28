using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Enrichment.Oribos
{
  // Resolved competitor data kept in the lookup maps.
  public record OribosEntry(
    string Bib, string? Card, string? Card2, string? Name, string? Class,
    string? Nation, string? ClubId, string? ClubName, string? ClubNation,
    DateTime? StartTime, string Status);

  public sealed class OribosService : IEnrichmentSource, IRadioSenderHost, IDisposable
  {
    private const double StartBeforeRaceStartThresholdSeconds = 3600 * 11;
    private const double StartBeforeRaceStartModuloSeconds = 3600 * 12;

    // Oribos status codes that mean "no longer racing" — used to disambiguate a card/bib
    // shared by more than one competitor.
    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
      { "CL", "NP", "SQ", "RI", "FT", "PE", "PM", "DI" };

    // Lazy to break the DI cycle: FilterService -> IEnrichmentSource (this) -> DispatcherService -> FilterService.
    // DispatcherService is only needed at runtime to publish status changes, not at construction.
    private readonly Lazy<DispatcherService> _dispatcherService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OribosEnrichmentConfiguration _configuration;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // lookup maps rebuilt on every fullweb fetch
    private volatile IReadOnlyDictionary<string, OribosEntry> _cardMap = new Dictionary<string, OribosEntry>();
    private volatile IReadOnlyDictionary<string, OribosEntry> _bibMap = new Dictionary<string, OribosEntry>();

    // status snapshot for change detection (only well-defined, non-ambiguous bibs)
    private Dictionary<string, string> _statusSnapshot = new();
    private bool _snapshotInitialized;

    // card→bib snapshot to log changes in the mapping (skips the initial load)
    private Dictionary<string, string> _cardBibSnapshot = new();
    private bool _mappingInitialized;

    // keys already warned about (ambiguous), to log only once
    private readonly HashSet<string> _warnedKeys = [];

    private CancellationTokenSource _cts = new();
    private Task? _executingTask;
    private string? _lastUpdate;

    private DateTimeOffset _lastFetch;

    public string Name => _configuration.Name;

    public OribosService(
      Lazy<DispatcherService> dispatcherService,
      IHttpClientFactory httpClientFactory,
      OribosEnrichmentConfiguration configuration)
    {
      _dispatcherService = dispatcherService;
      _httpClientFactory = httpClientFactory;
      _configuration = configuration;
    }

    #region enrichment

    public Punch Enrich(Punch punch)
    {
      OribosEntry? entry = punch.CompetitorIdType switch
      {
        CompetitorIdType.PunchingCard => Lookup(_cardMap, punch.CompetitorId),
        CompetitorIdType.BibNumber => Lookup(_bibMap, punch.CompetitorId),
        // unknown id type: try card first, then bib (best-effort)
        _ => Lookup(_cardMap, punch.CompetitorId) ?? Lookup(_bibMap, punch.CompetitorId),
      };

      if (entry == null)
        return punch; // best-effort: pass through unchanged, no log

      return punch with { Competitor = ToCompetitor(entry) };
    }

    private static Competitor ToCompetitor(OribosEntry e) => new(
      Bib: e.Bib,
      Card: e.Card,
      Card2: e.Card2,
      Name: e.Name,
      Class: e.Class,
      Nation: e.Nation,
      ClubId: e.ClubId,
      ClubName: e.ClubName,
      ClubNation: e.ClubNation,
      StartTime: e.StartTime);

    private static OribosEntry? Lookup(IReadOnlyDictionary<string, OribosEntry> map, string? key)
      => key != null && map.TryGetValue(key, out var e) ? e : null;

    #endregion

    #region lifecycle / longpolling

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _executingTask = ExecuteAsync(_cts.Token);
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _cts.Cancel();
      return _executingTask ?? Task.CompletedTask;
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
      await Task.Yield();

      if (string.IsNullOrWhiteSpace(_configuration.Host) || !_configuration.Host.StartsWith("http"))
      {
        Log.Error("Oribos enrichment '{name}': invalid Host '{host}'", _configuration.Name, _configuration.Host);
        return;
      }

      var host = _configuration.Host.Replace("http://localhost:", "http://127.0.0.1:").TrimEnd('/');

      Log.Information("Oribos enrichment '{name}' listening on {host} (emitStatusChanges={emit})",
        _configuration.Name, host, _configuration.EmitStatusChanges);

      while (!ct.IsCancellationRequested)
      {
        try
        {
          await PollOnce(host, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
          Log.Warning("Oribos enrichment '{name}' error: {message}", _configuration.Name, e.Message);
          await Task.Delay(5000, ct);
        }
      }
    }

    private async Task PollOnce(string host, CancellationToken ct)
    {
      using var longClient = _httpClientFactory.CreateClient();
      longClient.Timeout = TimeSpan.FromSeconds(70);

      var update = await longClient.GetFromJsonAsync<OrServerUpdate>(
        $"{host}/ORServer.lastupdate.jsp?u={_lastUpdate}", _jsonOptions, ct);

      var changed = _lastUpdate != update?.Update;
      var stale = DateTimeOffset.UtcNow - _lastFetch > TimeSpan.FromMinutes(2);

      if (changed || stale)
      {
        await FetchFullweb(host, ct);
        _lastUpdate = update?.Update;
      }
    }

    private async Task FetchFullweb(string host, CancellationToken ct)
    {
      using var client = _httpClientFactory.CreateClient();
      client.Timeout = TimeSpan.FromSeconds(10);

      var merged = _configuration.Merged ? "true" : "false";
      var data = await client.GetFromJsonAsync<OrServer>(
        $"{host}/ORServer.fullweb.jsp?courses=true&merged={merged}", _jsonOptions, ct);

      if (data == null)
        return;

      _lastFetch = DateTimeOffset.UtcNow;

      var (cardMap, bibMap, _) = BuildLookups(data, _warnedKeys);
      _cardMap = cardMap;
      _bibMap = bibMap;

      LogMappingChanges(cardMap);

      if (_configuration.EmitStatusChanges)
        DetectAndEmitStatusChanges(bibMap, data.Update);
    }

    // Logs additions/changes/removals in the card→bib mapping. The initial load is not logged.
    private void LogMappingChanges(IReadOnlyDictionary<string, OribosEntry> cardMap)
    {
      var current = cardMap.ToDictionary(kv => kv.Key, kv => kv.Value.Bib);

      if (_mappingInitialized)
      {
        foreach (var (card, bib) in current)
        {
          if (!_cardBibSnapshot.TryGetValue(card, out var prevBib))
            Log.Information("Oribos '{name}': card {card} mapped to bib {bib}", _configuration.Name, card, bib);
          else if (prevBib != bib)
            Log.Information("Oribos '{name}': card {card} remapped from bib {prev} to bib {bib}", _configuration.Name, card, prevBib, bib);
        }

        foreach (var (card, prevBib) in _cardBibSnapshot)
        {
          if (!current.ContainsKey(card))
            Log.Information("Oribos '{name}': card {card} unmapped (was bib {prev})", _configuration.Name, card, prevBib);
        }
      }

      _cardBibSnapshot = current;
      _mappingInitialized = true;
    }

    #endregion

    #region pure logic (testable)

    // Builds card→entry and bib→entry maps. A competitor's Card and Card2 both index to it.
    // Keys shared by more than one competitor are disambiguated by status (keep the only one
    // still racing); if still ambiguous the key is dropped and a one-time warning is logged.
    // Returns the maps and the count of ambiguous keys dropped.
    public static (IReadOnlyDictionary<string, OribosEntry> cardMap,
                   IReadOnlyDictionary<string, OribosEntry> bibMap,
                   int ambiguousCount) BuildLookups(OrServer data, HashSet<string>? warnedKeys = null)
    {
      var competitors = data.Competitors?.Where(c => c.Bib != null).ToList() ?? [];

      // clubId → club name (ClubId matches OrClub.CountryId)
      var clubsById = new Dictionary<string, string>();
      foreach (var club in data.Clubs ?? [])
      {
        if (!string.IsNullOrWhiteSpace(club.CountryId) && !string.IsNullOrWhiteSpace(club.Name))
          clubsById[club.CountryId] = club.Name;
      }

      // bib → entry
      var bibCandidates = new Dictionary<string, List<OrCompetitor>>();
      foreach (var c in competitors)
      {
        var bib = c.Bib!.Value.ToString(CultureInfo.InvariantCulture);
        if (!bibCandidates.TryGetValue(bib, out var list))
          bibCandidates[bib] = list = [];
        list.Add(c);
      }

      // card → entry (Card and Card2)
      var cardCandidates = new Dictionary<string, List<OrCompetitor>>();
      foreach (var c in competitors)
      {
        foreach (var card in new[] { c.Card, c.Card2 })
        {
          if (card == null || card.Value <= 0)
            continue;
          var key = card.Value.ToString(CultureInfo.InvariantCulture);
          if (!cardCandidates.TryGetValue(key, out var list))
            cardCandidates[key] = list = [];
          list.Add(c);
        }
      }

      var ambiguous = 0;
      var cardMap = Resolve(cardCandidates, data.Race.Startutc, clubsById, "card", warnedKeys, ref ambiguous);
      var bibMap = Resolve(bibCandidates, data.Race.Startutc, clubsById, "bib", warnedKeys, ref ambiguous);

      return (cardMap, bibMap, ambiguous);
    }

    private static IReadOnlyDictionary<string, OribosEntry> Resolve(
      Dictionary<string, List<OrCompetitor>> candidates,
      DateTimeOffset startutc,
      IReadOnlyDictionary<string, string> clubsById,
      string keyKind,
      HashSet<string>? warnedKeys,
      ref int ambiguous)
    {
      var map = new Dictionary<string, OribosEntry>();

      foreach (var (key, list) in candidates)
      {
        OrCompetitor? chosen;
        if (list.Count == 1)
        {
          chosen = list[0];
        }
        else
        {
          // disambiguate: keep only those still racing / to start
          var stillRacing = list.Where(c => !FinishedStatuses.Contains(c.Status ?? "")).ToList();
          chosen = stillRacing.Count == 1 ? stillRacing[0] : null;
        }

        if (chosen == null)
        {
          ambiguous++;
          if (warnedKeys != null && warnedKeys.Add($"{keyKind}:{key}"))
            Log.Warning("Oribos: ambiguous {kind} {key} maps to multiple racing competitors; not mapped", keyKind, key);
          continue;
        }

        // a previously-ambiguous key resolved → allow warning again if it recurs
        warnedKeys?.Remove($"{keyKind}:{key}");
        map[key] = ToEntry(chosen, startutc, clubsById);
      }

      return map;
    }

    private static OribosEntry ToEntry(OrCompetitor c, DateTimeOffset startutc, IReadOnlyDictionary<string, string> clubsById)
    {
      var bib = c.Bib!.Value.ToString(CultureInfo.InvariantCulture);
      var card = c.Card is int cv && cv > 0 ? cv.ToString(CultureInfo.InvariantCulture) : null;
      var card2 = c.Card2 is int cv2 && cv2 > 0 ? cv2.ToString(CultureInfo.InvariantCulture) : null;
      var fullName = string.Join(" ", new[] { c.Name, c.Surname }.Where(s => !string.IsNullOrWhiteSpace(s)));

      string? clubName = null;
      if (!string.IsNullOrWhiteSpace(c.ClubId) && clubsById.TryGetValue(c.ClubId, out var n))
        clubName = n;

      return new OribosEntry(
        Bib: bib,
        Card: card,
        Card2: card2,
        Name: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
        Class: c.Class,
        Nation: string.IsNullOrWhiteSpace(c.Naz) ? null : c.Naz,
        ClubId: string.IsNullOrWhiteSpace(c.ClubId) ? null : c.ClubId,
        ClubName: clubName,
        ClubNation: string.IsNullOrWhiteSpace(c.ClubCountry) ? null : c.ClubCountry,
        StartTime: AbsoluteStart(startutc, c.Start),
        Status: c.Status ?? "");
    }

    // Oribos Start is seconds relative to race start; values above 11h are wrapped (start
    // before race "hour 0"). Returns absolute local time, or null when not set.
    public static DateTime? AbsoluteStart(DateTimeOffset startutc, double start)
    {
      if (start <= 0)
        return null;
      return (startutc + TimeSpan.FromSeconds(NormalizeRelativeStart(start))).LocalDateTime;
    }

    public static double NormalizeRelativeStart(double start)
      => start > StartBeforeRaceStartThresholdSeconds ? start - StartBeforeRaceStartModuloSeconds : start;

    // Oribos status code → RadioSender CompetitorStatus. Null = ignored (no enum / no event).
    public static CompetitorStatus? MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
      "PE" or "PM" => CompetitorStatus.MP,
      "NP" => CompetitorStatus.DNS,
      "SQ" => CompetitorStatus.DSQ,
      "RI" => CompetitorStatus.DNF,
      "FT" => CompetitorStatus.OverTime,
      "CL" => CompetitorStatus.OK,
      "GA" => CompetitorStatus.Running,
      "IP" => CompetitorStatus.WaitingStart,
      _ => null,
    };

    // Only "anomalous" outcomes are emitted as events when newly detected.
    private static readonly HashSet<CompetitorStatus> EmittableStatuses =
      [CompetitorStatus.MP, CompetitorStatus.DNS, CompetitorStatus.DSQ, CompetitorStatus.DNF, CompetitorStatus.OverTime];

    #endregion

    #region status change detection

    private void DetectAndEmitStatusChanges(IReadOnlyDictionary<string, OribosEntry> bibMap, DateTimeOffset update)
    {
      var newSnapshot = new Dictionary<string, string>();
      var toEmit = new List<Punch>();

      foreach (var (bib, entry) in bibMap)
      {
        newSnapshot[bib] = entry.Status;

        if (!_snapshotInitialized)
          continue; // first fetch: just initialize, never emit

        // emit only on a real transition to an emittable, anomalous status
        var changed = !_statusSnapshot.TryGetValue(bib, out var prev) || prev != entry.Status;
        if (!changed)
          continue;

        var mapped = MapStatus(entry.Status);
        if (mapped is not CompetitorStatus status || !EmittableStatuses.Contains(status))
          continue;

        toEmit.Add(new Punch(
          CompetitorId: bib,
          CompetitorIdType: CompetitorIdType.BibNumber,
          Time: update.LocalDateTime,
          Control: 0,
          ControlType: PunchControlType.Unknown,
          SourceId: _configuration.Name,
          ReceivedAt: DateTimeOffset.UtcNow,
          CompetitorStatus: status,
          Cancellation: false,
          Competitor: ToCompetitor(entry)));
      }

      _statusSnapshot = newSnapshot;
      _snapshotInitialized = true;

      if (toEmit.Count > 0)
      {
        Log.Information("Oribos enrichment '{name}' emitting {count} status change(s)", _configuration.Name, toEmit.Count);
        _dispatcherService.Value.PushDispatch(new PunchDispatch(Punches: toEmit));
      }
    }

    #endregion

    public void Dispose()
    {
      _cts.Cancel();
      _cts.Dispose();
      _executingTask?.Dispose();
    }
  }
}

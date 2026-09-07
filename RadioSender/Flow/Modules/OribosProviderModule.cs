using RadioSender.Hosts.Common;
using RadioSender.Hosts.Enrichment;
using RadioSender.Hosts.Enrichment.Oribos;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public sealed record OribosProviderSettings
{
  [Required, Url, Display(Name = "Oribos server")]
  public string Host { get; init; } = "http://127.0.0.1:8080";
  public bool Merged { get; init; }
  [Display(Name = "Emit status changes")]
  public bool EmitStatusChanges { get; init; }
}
public sealed record EnrichmentSettings
{
  [Required, Display(Name = "Provider")]
  public string ProviderId { get; init; } = "";
}
public sealed class EnrichmentModule(EnrichmentSettings settings, ModuleContext context) : FlowModule
{
  public override Punch? Process(Punch punch, bool replay) =>
    (context.Resolve?.Invoke(settings.ProviderId) as IEnrichmentSource)?.Enrich(punch)
      ?? throw new FlowException("The enrichment provider is unavailable.");
}

public sealed class OribosProviderModule(OribosProviderSettings settings, ModuleContext context) : FlowModule, IEnrichmentSource
{
  private sealed record Lookup(IReadOnlyDictionary<string, OribosEntry> Cards, IReadOnlyDictionary<string, OribosEntry> Bibs);
  private Lookup _lookup = new(new Dictionary<string, OribosEntry>(), new Dictionary<string, OribosEntry>());
  private readonly CancellationTokenSource _lifetime = new();
  private readonly SemaphoreSlim _fetchGate = new(1, 1);
  private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(70), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
  private Task? _poll, _refresh;
  private bool _disposed, _initialized;
  private Dictionary<string, string> _statuses = [];
  private string? _update;
  private DateTimeOffset _lastFetch;
  public string Name => context.NodeId;
  public Punch Enrich(Punch punch)
  { var lookup = Volatile.Read(ref _lookup); return OribosService.Enrich(punch, lookup.Cards, lookup.Bibs); }
  public override Task StartAsync(CancellationToken ct)
  { SetState("Loading"); _poll = PollAsync(); return Task.CompletedTask; }
  private async Task PollAsync()
  {
    try
    {
      while (!_lifetime.IsCancellationRequested)
      {
        try
        {
          if (!_initialized) await FetchAsync(_lifetime.Token);
          var update = await ReadAsync<OrServerUpdate>(settings.Host.TrimEnd('/') + "/ORServer.lastupdate.jsp?u=" + Uri.EscapeDataString(_update ?? ""), _lifetime.Token);
          if (update?.Update != _update || DateTimeOffset.UtcNow - _lastFetch > TimeSpan.FromMinutes(2))
          { await FetchAsync(_lifetime.Token); _update = update?.Update; }
          await Task.Delay(200, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
        catch (Exception)
        { SetState("Disconnected", "Unable to refresh Oribos data; retaining the last snapshot."); await Task.Delay(5000, _lifetime.Token); }
      }
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
  }
  private async Task<T?> ReadAsync<T>(string url, CancellationToken ct)
  {
    using var response = await _client.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<T>(FlowJson.Options, ct);
  }
  private async Task FetchAsync(CancellationToken ct)
  {
    await _fetchGate.WaitAsync(ct);
    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
      timeout.CancelAfter(TimeSpan.FromSeconds(10));
      var data = await ReadAsync<OrServer>(settings.Host.TrimEnd('/') + $"/ORServer.fullweb.jsp?courses=true&merged={settings.Merged.ToString().ToLowerInvariant()}", timeout.Token)
        ?? throw new FlowException("Oribos returned an empty response.");
      var (cards, bibs, _) = OribosService.BuildLookups(data);
      Volatile.Write(ref _lookup, new(cards, bibs));
      _lastFetch = DateTimeOffset.UtcNow;
      var (snapshot, changes) = OribosService.ComputeStatusChanges(bibs, _statuses, _initialized);
      _statuses = snapshot; _initialized = true;
      if (settings.EmitStatusChanges)
        foreach (var (entry, status, finish) in changes)
        {
          var punch = new Punch(entry.Bib, finish ? entry.FinishTime!.Value : data.Update.LocalDateTime, 10, context.NodeId, DateTimeOffset.UtcNow,
            CompetitorIdType.BibNumber, finish ? PunchControlType.Finish : PunchControlType.Unknown, status);
          await context.Output.PublishAsync(Enrich(punch), timeout.Token);
        }
      SetState("Running", $"{bibs.Count} competitors, {cards.Count} cards. Updated {_lastFetch:HH:mm:ss}.");
    }
    finally { _fetchGate.Release(); }
  }
  public override object? ViewData() => new { Competitors = _lookup.Bibs.Count, Cards = _lookup.Cards.Count, UpdatedAt = _lastFetch };
  public override async ValueTask<CommandResult> ExecuteCommandAsync(string command, JsonObject arguments, CancellationToken ct)
  {
    if (command != "refresh") return await base.ExecuteCommandAsync(command, arguments, ct);
    if (_refresh == null || _refresh.IsCompleted)
      _refresh = Task.Run(async () =>
      {
        try { await FetchAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { SetState("Disconnected", "Unable to refresh Oribos data; retaining the last snapshot."); }
      }, CancellationToken.None);
    return new("Oribos refresh requested.");
  }
  public override async Task StopAsync(CancellationToken ct)
  {
    if (_disposed) return;
    await _lifetime.CancelAsync();
    if (_poll != null) await _poll;
    if (_refresh != null) await _refresh;
    SetState("Stopped");
  }
  public override async ValueTask DisposeAsync()
  { if (_disposed) return; await StopAsync(default); _disposed = true; _client.Dispose(); _fetchGate.Dispose(); _lifetime.Dispose(); }
}

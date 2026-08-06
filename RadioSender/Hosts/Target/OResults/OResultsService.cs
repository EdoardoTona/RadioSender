using Hangfire;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.OResults
{
  // Body of POST /punches/external (https://api.oresults.eu/api-docs)
  public record OResultsRequest(
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("records")] IReadOnlyList<OResultsPunch> Records);

  public record OResultsPunch(
    [property: JsonPropertyName("card")] long Card,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("punch_type")] int? PunchType);

  public sealed class OResultsService : ITarget
  {
    private readonly FilterService _filterService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private static IHttpClientFactory? _httpClientFactory; // static for hangfire
    private readonly OResultsConfiguration _configuration;

    public OResultsService(
      FilterService filterService,
      IBackgroundJobClient backgroundJobClient,
      IHttpClientFactory httpClientFactory,
      OResultsConfiguration configuration)
    {
      _filterService = filterService;
      _backgroundJobClient = backgroundJobClient;
      _httpClientFactory = httpClientFactory;
      _configuration = configuration;
    }

    public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        SendDispatch(dispatch, ct);

      return Task.CompletedTask;
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null)
        return Task.CompletedTask;

      var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

      var records = punches
        .Select(p => ToRecord(p, _configuration.UseUtc, _configuration.IgnoreCompetitorIdType))
        .Where(r => r != null)
        .Select(r => r!)
        .ToList();

      if (records.Count == 0)
        return Task.CompletedTask;

      _backgroundJobClient.Enqueue(() => SendRecordsAction(_configuration, records, default));

      return Task.CompletedTask;
    }

    private static OResultsPunch? ToRecord(Punch punch, bool useUtc, bool ignoreCompetitorIdType)
    {
      // OResults does not support cancellations
      if (punch.Cancellation)
        return null;

      // OResults wants the SI card number. Prefer the card resolved by enrichment;
      // fall back to CompetitorId only when no specific card is available
      // (valid only if the punch itself is a punching card, unless the type check is disabled).
      var cardStr = punch.Competitor?.Card
        ?? (ignoreCompetitorIdType || punch.CompetitorIdType is CompetitorIdType.PunchingCard or CompetitorIdType.Unknown ? punch.CompetitorId : null);

      if (!long.TryParse(cardStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var card))
      {
        Log.Warning("OResults requires a numeric card number, got competitorId '{competitorId}' (type {type}) with no enriched card. Ignored",
          punch.CompetitorId, punch.CompetitorIdType);
        return null;
      }

      // OResults punch_type: 0=Control, 1=Start, 2=Clear, 3=Check, 9=Finish
      int? punchType = punch.ControlType switch
      {
        PunchControlType.Control => 0,
        PunchControlType.Start => 1,
        PunchControlType.Clear => 2,
        PunchControlType.Check => 3,
        PunchControlType.Finish => 9,
        _ => null,
      };

      // RFC3339: UTC with 'Z', or event-local time without offset
      var time = useUtc
        ? punch.Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        : punch.Time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

      return new OResultsPunch(card, punch.Control, time, punchType);
    }

    public static async Task SendRecordsAction(OResultsConfiguration _configuration, IReadOnlyList<OResultsPunch> records, CancellationToken ct = default)
    {
      if (string.IsNullOrEmpty(_configuration.ApiKey) || _httpClientFactory == null)
        throw new ArgumentException("Missing OResults ApiKey");

      using var httpClient = _httpClientFactory.CreateClient();

      var host = _configuration.Host.Contains("localhost") ? _configuration.Host.Replace("localhost", "127.0.0.1") : _configuration.Host; // optimization to skip the dns resolution
      httpClient.BaseAddress = new Uri(host);

      var body = new OResultsRequest(_configuration.ApiKey, records);

      HttpResponseMessage response;
      try
      {
        response = await httpClient.PostAsJsonAsync(_configuration.Path, body, ct);
      }
      catch
      {
        Log.Warning("OResults not reachable: {host}", host);
        throw;
      }

      if (!response.IsSuccessStatusCode)
      {
        var text = await response.Content.ReadAsStringAsync(ct);
        Log.Warning("OResults responded {status}: {body}", response.StatusCode, text);
        response.EnsureSuccessStatusCode();
      }
    }
  }
}

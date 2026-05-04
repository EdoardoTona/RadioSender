using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SportidentCenter
{
  public record SimplePunch(long Id, long Card, long Time, int Code, string Mode);
  public class SportidentCenterEvent(
    FilterService filterService,
    IHttpClientFactory clientFactory,
    DispatcherService dispatcherService,
    Event configuration) : IRadioSenderHost, ISource, IDisposable
  {
    public const string HTTPCLIENT_NAME = "sportident";
    private readonly HttpClient _httpClient = clientFactory.CreateClient(HTTPCLIENT_NAME);
    private readonly int _refreshInterval_ms = configuration.RefreshMs;

    private long _lastReceivedId;

    private readonly CsvConfiguration _csvReaderConfiguration = new(CultureInfo.InvariantCulture)
    {
      PrepareHeaderForMatch = args => args.Header.ToLower(CultureInfo.InvariantCulture)
    };

    private CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _executingtask;

    private DateTimeOffset lastDiagnosticReport;
    private ConcurrentBag<long> diagnosticTimes = [];
    private bool wasError = false;

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _executingtask = ExecuteAsync(_cts.Token);
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _cts.Cancel();
      return _executingtask ?? Task.CompletedTask;
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
      await Task.Yield();

      Log.Information("Sportident center listening event {event}. Frequency {frequency}", configuration.EventId, configuration.RefreshMs);

      if (configuration.EventId == 0 || configuration.EventId == null || string.IsNullOrEmpty(configuration.ApiKey))
      {
        Log.Error("Sportident center: EventId/ApiKey missing");
      }

      while (!ct.IsCancellationRequested)
      {
        try
        {
          await GetData(ct);
          await Task.Delay(_refreshInterval_ms, ct);
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
          Log.Error("Error getting data from SportidentCenter: {message}", e.Message);
        }
      }
    }

    private async Task GetData(CancellationToken ct)
    {
      try
      {
        if (configuration.EventId == 0 || configuration.EventId == null || string.IsNullOrEmpty(configuration.ApiKey))
        {
          return;
        }
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rest/v1/public/events/{configuration.EventId}/punches?projection=simple&afterId={_lastReceivedId}");

        request.Headers.Add("apikey", configuration.ApiKey);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("text/csv"));

        var sw = new Stopwatch();
        sw.Start();
        var response = await _httpClient.SendAsync(request, ct);
        sw.Stop();

        try
        {
          diagnosticTimes.Add(sw.ElapsedMilliseconds);
          if (DateTimeOffset.UtcNow - lastDiagnosticReport > TimeSpan.FromMinutes(2))
          {
            Log.Information("SportidentCenter diagnostic report for event {event}: count {count}, avg {avg:0}ms, min {min}ms, max {max}ms",
              configuration.EventId, diagnosticTimes.Count, diagnosticTimes.Average(), diagnosticTimes.Min(), diagnosticTimes.Max());

            lastDiagnosticReport = DateTimeOffset.UtcNow;
            diagnosticTimes.Clear();
          }
        }
        catch
        {
          Log.Warning("Error writing diagnostic report for SportidentCenter event {event}", configuration.EventId);
        }

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          var list = csv.GetRecords<SimplePunch>().OrderBy(p => p.Time).ToList();

          if (wasError)
          {
            Log.Information("SportidentCenter event {event} recovered", configuration.EventId);
            wasError = false;
          }

          IEnumerable<Punch>? punches = null;
          if (list.Count != 0)
          {
            punches = filterService.Transform(
                      configuration.Filter,
                        list.Select(p =>
                              new Punch(

                      ReceivedAt: DateTimeOffset.UtcNow,
                               CompetitorId: p.Card.ToString(),
                               CompetitorIdType: CompetitorIdType.PunchingCard,
                               Control: p.Code,
                               ControlType: MapControlType(p.Mode),
                               Time: DateTimeOffset.FromUnixTimeMilliseconds(p.Time).DateTime,
                               SourceId: HTTPCLIENT_NAME
                              )
                      )
                    );

            _lastReceivedId = list.Last().Id;
          }

          dispatcherService.PushDispatch(
                      new PunchDispatch(
                        Punches: punches,
                        Nodes: [new NodeNew(HTTPCLIENT_NAME, HTTPCLIENT_NAME, sw.ElapsedMilliseconds + _refreshInterval_ms, 1)],
                        Hops: [new Hop(HTTPCLIENT_NAME, NodeNew.Localhost.Id, sw.ElapsedMilliseconds + _refreshInterval_ms, 1)]
                      )
            );

        }
        else
        {
          wasError = true;
          Log.Error("Error getting data from SportidentCenter (event {event}): response code {code}", configuration.EventId, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        wasError = true;
        Log.Error("Error getting data from SportidentCenter (event {event}): {message}", configuration.EventId, e.Message);
      }
    }


    private static PunchControlType MapControlType(string controlType)
    {
      var ct = controlType.ToLowerInvariant();
      if (ct.Contains("control"))
        return PunchControlType.Control;

      if (ct.Contains("finish"))
        return PunchControlType.Finish;

      if (ct.Contains("start"))
        return PunchControlType.Start;

      if (ct.Contains("check"))
        return PunchControlType.Check;

      if (ct.Contains("clear"))
        return PunchControlType.Clear;

      return PunchControlType.Unknown;
    }

    public void Dispose()
    {
      _cts.Cancel();
      _cts.Dispose();
      _httpClient.Dispose();

    }
  }
}

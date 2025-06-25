using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.ROC
{
  public record ROCPunch(long Id, int Code, long Card, DateTime Time);
  public sealed class ROCEvent : IRadioSenderHost, ISource, IDisposable
  {
    public const string HTTPCLIENT_NAME = "roc";
    private readonly HttpClient _httpClient;
    private readonly DispatcherService _dispatcherService;
    private readonly FilterService _filterService;
    private readonly int _eventId;

    private readonly Event? _configuration;

    private long _lastReceivedId;

    private readonly CsvConfiguration _csvReaderConfiguration;

    private CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _executingtask;

    private DateTimeOffset lastDiagnosticReport;
    private ConcurrentBag<long> diagnosticTimes = [];
    private bool wasError = false;

    public ROCEvent(
      IHttpClientFactory clientFactory,
      DispatcherService dispatcherService,
      FilterService filterService,
      Event configuration,
      int eventId)
    {
      _httpClient = clientFactory.CreateClient(HTTPCLIENT_NAME + eventId);
      _dispatcherService = dispatcherService;
      _filterService = filterService;
      _eventId = eventId;
      _csvReaderConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HasHeaderRecord = false,
        Delimiter = ";"
      };

      _configuration = configuration;

    }

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

      Log.Information("ROC center listening event {event} on {server}. Frequency {frequency}", _configuration?.EventId, _configuration.Host, _configuration?.RefreshMs);

      while (!ct.IsCancellationRequested)
      {
        try
        {
          await GetData(ct);
          await Task.Delay(_configuration?.RefreshMs ?? 10_000, ct);
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
          Log.Error("Error getting data from ROC: {message}", e.Message);
        }
      }
    }

    public async Task GetData(CancellationToken ct)
    {
      try
      {
        if (_configuration == null || _configuration.EventId == null)
        {
          Log.Error("No EventId");
          return;
        }

        if (!_configuration.Enable)
          return;

        var path = _configuration.Path.Replace("{EventId}", _configuration.EventId.ToString()).Replace("{LastId}", _lastReceivedId.ToString());

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var sw = new Stopwatch();
        sw.Start();
        var response = await _httpClient.SendAsync(request, ct);
        sw.Stop();

        try
        {
          diagnosticTimes.Add(sw.ElapsedMilliseconds);
          if (DateTimeOffset.UtcNow - lastDiagnosticReport > TimeSpan.FromMinutes(2))
          {
            Log.Information("ROC diagnostic report for event {event}: count {count}, avg {avg:0}ms, min {min}ms, max {max}ms",
              _configuration.EventId, diagnosticTimes.Count, diagnosticTimes.Average(), diagnosticTimes.Min(), diagnosticTimes.Max());

            lastDiagnosticReport = DateTimeOffset.UtcNow;
            diagnosticTimes.Clear();
          }
        }
        catch
        {
          Log.Warning("Error writing diagnostic report for ROC event {event}", _configuration.EventId);
        }
        if (response.IsSuccessStatusCode)
        {

          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);

          if (wasError)
          {
            Log.Information("ROC event {event} recovered", _configuration.EventId);
            wasError = false;
          }

          var list = csv.GetRecords<ROCPunch>().OrderBy(p => p.Time).ToList();
          IEnumerable<Punch>? punches = null;
          if (list.Count != 0)
          {
            punches = _filterService.Transform(
                        _configuration.Filter,
                        list.Select(p =>
                        new Punch(
                      ReceivedAt: DateTimeOffset.UtcNow,
                          Card: p.Card.ToString(),
                          Time: p.Time,
                          Control: p.Code,
                          ControlType: PunchControlType.Unknown,
                          SourceId: HTTPCLIENT_NAME + _eventId
                        )
                      )
                    );

            _lastReceivedId = list.Last().Id;
          }

          _dispatcherService.PushDispatch(
                      new PunchDispatch(
                        Punches: punches,
                        Nodes: [new NodeNew(HTTPCLIENT_NAME + _eventId, HTTPCLIENT_NAME + _eventId, sw.ElapsedMilliseconds + _configuration.RefreshMs, 1)],
                        Hops: [new Hop(HTTPCLIENT_NAME + _eventId, NodeNew.Localhost.Id, sw.ElapsedMilliseconds + _configuration.RefreshMs, 1)]
                      )
            );

        }
        else
        {
          wasError = true;
          Log.Error("Error getting data from ROC (event {event}): response code {code}", _configuration.EventId, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        wasError = true;
        Log.Error("Error getting data from ROC (event {event}): {message}", _configuration, e.Message);
      }
    }

    public void Dispose()
    {
      _cts.Cancel();
      _cts.Dispose();
      _httpClient.Dispose();
      _executingtask?.Dispose();
    }
  }
}

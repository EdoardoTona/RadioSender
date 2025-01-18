using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
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
  public class ROCEvent : BackgroundService, ISource
  {
    public const string HTTPCLIENT_NAME = "roc";
    private readonly HttpClient _httpClient;
    private readonly Event _configuration;
    private readonly DispatcherService _dispatcherService;
    private readonly int _refreshInterval_ms;
    private readonly IFilter _filter = Filter.Invariant;

    private long _lastReceivedId;

    private readonly CsvConfiguration _csvReaderConfiguration;

    public ROCEvent(
      IEnumerable<IFilter> filters,
      IHttpClientFactory clientFactory,
      DispatcherService dispatcherService,
      Event rocEvent)
    {
      _httpClient = clientFactory.CreateClient(HTTPCLIENT_NAME);
      _configuration = rocEvent;
      _dispatcherService = dispatcherService;

      _csvReaderConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HasHeaderRecord = false,
        Delimiter = ";"
      };
      _refreshInterval_ms = rocEvent.RefreshMs;
      _filter = filters.GetFilter(_configuration.Filter);
    }


    protected override async Task ExecuteAsync(CancellationToken ct)
    {
      await Task.Yield();

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
          Log.Error("Error getting data from ROC: {message}", e.Message);
        }
      }
    }

    public async Task GetData(CancellationToken ct)
    {
      try
      {
        if (_configuration.EventId == null)
        {
          Log.Error("No EventId");
          return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/getPunches.asp?unitId={_configuration.EventId}&lastId={_lastReceivedId}");
        var sw = new Stopwatch();
        sw.Start();
        var response = await _httpClient.SendAsync(request, ct);
        sw.Stop();
        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);

          var list = csv.GetRecords<ROCPunch>().OrderBy(p => p.Time).ToList();
          IEnumerable<Punch>? punches = null;
          if (list.Any())
          {
            punches = _filter.Transform(
                        list.Select(p =>
                        new Punch(
                      ReceivedAt: DateTimeOffset.UtcNow,
                          Card: p.Card.ToString(),
                          Time: p.Time,
                          Control: p.Code,
                          ControlType: PunchControlType.Unknown,
                          SourceId: HTTPCLIENT_NAME
                        )
                      )
                    );

            _lastReceivedId = list.Last().Id;
          }

          _dispatcherService.PushDispatch(
                      new PunchDispatch(
                        Punches: punches,
                        Nodes: new[] { new NodeNew(HTTPCLIENT_NAME, HTTPCLIENT_NAME, sw.ElapsedMilliseconds + _refreshInterval_ms, 1) },
                        Hops: new[] { new Hop(HTTPCLIENT_NAME, NodeNew.Localhost.Id, sw.ElapsedMilliseconds + _refreshInterval_ms, 1) }
                      )
            );

        }
        else
        {
          Log.Error("Error getting data from ROC (event {event}): response code {code}", _configuration, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error("Error getting data from ROC (event {event}): {message}", _configuration, e.Message);
      }
    }

  }
}

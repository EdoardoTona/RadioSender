using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.ROC
{
  public class ROCEvent : BackgroundService, ISource
  {
    public const string HTTPCLIENT_NAME = "roc";
    private readonly HttpClient _httpClient;
    private readonly Event _configuration;
    private readonly DispatcherService _dispatcherService;
    private readonly int _refreshInterval_ms;
    private readonly IFilter _filter = Filter.Invariant;

    private long _lastReceivedId = 0;

    private CsvConfiguration _csvReaderConfiguration;

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
        var request = new HttpRequestMessage(HttpMethod.Get, $"/getPunches.asp?unitId={_configuration.EventId}&lastId={_lastReceivedId}");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          IEnumerable<ROCPunch> punches = csv.GetRecords<ROCPunch>().ToList();

          if (!punches.Any())
            return;

          _lastReceivedId = punches.OrderBy(p => p.Time).Last().Id;

          _dispatcherService.PushPunches(
            _filter.Transform(
                    punches.Select(p =>
                    new Punch()
                    {
                      Card = p.Card.ToString(),
                      Time = p.Time,
                      Control = p.Code,
                      ControlType = PunchControlType.Unknown
                    })
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

using CsvHelper;
using CsvHelper.Configuration;
using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.ROC
{
  public class ROCEvent
  {
    private readonly HttpClient _httpClient;
    private readonly Event _rocEvent;
    private readonly DispatcherService _dispatcherService;

    private long _lastReceivedId = 0;

    private CsvConfiguration _csvReaderConfiguration;

    public ROCEvent(HttpClient httpClient, DispatcherService dispatcherService, Event rocEvent)
    {
      _httpClient = httpClient;
      _rocEvent = rocEvent;
      _dispatcherService = dispatcherService;

      _csvReaderConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HasHeaderRecord = false,
        Delimiter = ";"
      };
    }

    public async Task GetData(CancellationToken ct)
    {
      try
      {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/getPunches.asp?unitId={_rocEvent.EventId}&lastId={_lastReceivedId}");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          IEnumerable<ROCPunch> punches = csv.GetRecords<ROCPunch>().ToList();

          if (!punches.Any())
            return;

          if (_rocEvent.IgnoreOlderThan > TimeSpan.Zero)
          {
            var limit = DateTimeOffset.UtcNow - _rocEvent.IgnoreOlderThan;
            punches = punches.Where(p => { var dto = new DateTimeOffset(p.Time); return dto > limit; });

            if (!punches.Any())
              return;
          }

          _lastReceivedId = punches.OrderBy(p => p.Time).Last().Id;

          _dispatcherService.PushPunches(punches.Select(p =>
          new Punch(
            p.Card.ToString(),
            p.Time,
            p.Code,
            PunchControlType.Unknown
            )));

        }
        else
        {
          Log.Error("Error getting data from ROC (event {event}): response code {code}", _rocEvent, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error("Error getting data from ROC (event {event}): {message}", _rocEvent, e.Message);
      }
    }

  }
}

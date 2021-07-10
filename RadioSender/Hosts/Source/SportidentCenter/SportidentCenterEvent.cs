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

namespace RadioSender.Hosts.Source.SportidentCenter
{
  public class SportidentCenterEvent
  {
    private readonly HttpClient _httpClient;
    private readonly Event _siEvent;
    private readonly DispatcherService _dispatcherService;

    private long _lastReceivedId = 0;

    private CsvConfiguration _csvReaderConfiguration;

    public SportidentCenterEvent(HttpClient httpClient, DispatcherService dispatcherService, Event siEvent)
    {
      _httpClient = httpClient;
      _siEvent = siEvent;
      _dispatcherService = dispatcherService;

      _csvReaderConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        PrepareHeaderForMatch = args => args.Header.ToLower()
      };
    }

    public async Task GetData(CancellationToken ct)
    {
      try
      {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rest/v1/public/events/{_siEvent.EventId}/punches?projection=simple&afterId={_lastReceivedId}");

        request.Headers.Add("apikey", _siEvent.ApiKey);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("text/csv"));

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          //var punches = await JsonSerializer.DeserializeAsync<IEnumerable<SimplePunch>>(responseStream,
          //                                   new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          IEnumerable<ROCPunch> punches = csv.GetRecords<ROCPunch>().ToList();

          if (!punches.Any())
            return;

          if (_siEvent.IgnoreOlderThan > TimeSpan.Zero)
          {
            var limit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _siEvent.IgnoreOlderThan.TotalMilliseconds;
            punches = punches.Where(p => p.Time > limit);

            if (!punches.Any())
              return;
          }

          _lastReceivedId = punches.OrderBy(p => p.Time).Last().Id;

          _dispatcherService.PushPunches(punches.Select(p =>
          new Punch(
            p.Card.ToString(),
            DateTimeOffset.FromUnixTimeMilliseconds(p.Time).DateTime,
            p.Code,
            MapControlType(p.Mode)
            )));

        }
        else
        {
          Log.Error("Error getting data from SportidentCenter (event {event}): response code {code}", _siEvent, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error("Error getting data from SportidentCenter (event {event}): {message}", _siEvent, e.Message);
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
  }
}

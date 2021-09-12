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
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SportidentCenter
{
  public record SimplePunch(long Id, long Card, long Time, int Code, string Mode);
  public class SportidentCenterEvent : BackgroundService, ISource
  {
    public const string HTTPCLIENT_NAME = "sportident";
    private readonly IFilter _filter;
    private readonly HttpClient _httpClient;
    private readonly Event _configuration;
    private readonly DispatcherService _dispatcherService;
    private readonly int _refreshInterval_ms;

    private long _lastReceivedId;

    private readonly CsvConfiguration _csvReaderConfiguration;

    public SportidentCenterEvent(
      IEnumerable<IFilter> filters,
      IHttpClientFactory clientFactory,
      DispatcherService dispatcherService,
      Event siEvent)
    {
      _httpClient = clientFactory.CreateClient(HTTPCLIENT_NAME);
      _configuration = siEvent;
      _dispatcherService = dispatcherService;

      _csvReaderConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        PrepareHeaderForMatch = args => args.Header.ToLower(CultureInfo.InvariantCulture)
      };
      _refreshInterval_ms = siEvent.RefreshMs;
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
          Log.Error("Error getting data from SportidentCenter: {message}", e.Message);
        }
      }
    }

    private async Task GetData(CancellationToken ct)
    {
      try
      {
        if (_configuration.EventId == null || _configuration.ApiKey == null)
        {
          Log.Error("No EventId/ApiKey");
          return;
        }
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rest/v1/public/events/{_configuration.EventId}/punches?projection=simple&afterId={_lastReceivedId}");

        request.Headers.Add("apikey", _configuration.ApiKey);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("text/csv"));

        var sw = new Stopwatch();
        sw.Start();
        var response = await _httpClient.SendAsync(request, ct);
        sw.Stop();

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          //var punches = await JsonSerializer.DeserializeAsync<IEnumerable<SimplePunch>>(responseStream,
          //                                   new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          IEnumerable<SimplePunch> list = csv.GetRecords<SimplePunch>().OrderBy(p => p.Time).ToList();

          IEnumerable<Punch>? punches = null;
          if (list.Any())
          {
            punches = _filter.Transform(
                        list.Select(p =>
                              new Punch(
                               Card: p.Card.ToString(),
                               Control: p.Code,
                               ControlType: MapControlType(p.Mode),
                               Time: DateTimeOffset.FromUnixTimeMilliseconds(p.Time).DateTime,
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
          Log.Error("Error getting data from SportidentCenter (event {event}): response code {code}", _configuration, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error("Error getting data from SportidentCenter (event {event}): {message}", _configuration, e.Message);
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

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
    private readonly IFilter _filter = Filter.Invariant;
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

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          //var punches = await JsonSerializer.DeserializeAsync<IEnumerable<SimplePunch>>(responseStream,
          //                                   new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, ct);

          using var reader = new StreamReader(responseStream, Encoding.UTF8);
          using var csv = new CsvReader(reader, _csvReaderConfiguration);
          IEnumerable<SimplePunch> punches = csv.GetRecords<SimplePunch>().ToList();

          if (!punches.Any())
            return;

          _lastReceivedId = punches.OrderBy(p => p.Time).Last().Id;

          _dispatcherService.PushPunch(
                      new PunchDispatch(
                        _filter.Transform(
                          punches.Select(p =>
                              new Punch(
                               Card: p.Card.ToString(),
                               Control: p.Code,
                               ControlType: MapControlType(p.Mode),
                               Time: DateTimeOffset.FromUnixTimeMilliseconds(p.Time).DateTime
                              )
                          )
                        )
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

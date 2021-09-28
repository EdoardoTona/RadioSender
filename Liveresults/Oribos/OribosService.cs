using Liveresults.Models;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Liveresults.Oribos
{

  public class OribosService : BackgroundService, IDisposable
  {
    private readonly HttpClient _httpClient;
    private readonly OribosServer _configuration;

    private string _lastUpdate = "";

    private readonly Subject<object?> _changes = new();
    private readonly IDisposable sub;

    private readonly CategoryService _categoryService;
    private readonly ResultsService _resultsService;

    public OribosService(
      IHttpClientFactory httpClientFactory,
      OribosServer configuration,
      CategoryService categoryService,
      ResultsService resultsService
      )
    {
      _httpClient = httpClientFactory.CreateClient();
      _configuration = configuration;

      var host = _configuration.Host.Contains("localhost") ? _configuration.Host.Replace("localhost", "127.0.0.1") : _configuration.Host; // optimization to skip the dns resolution
      _httpClient.BaseAddress = new Uri(host);
      _httpClient.Timeout = TimeSpan.FromSeconds(45);


      sub = _changes.Throttle(TimeSpan.FromMilliseconds(100))
                        .Do(async _ => await OnUpdate(default))
                        .Subscribe();
      _categoryService = categoryService;
      _resultsService = resultsService;

    }

    public override void Dispose()
    {
      base.Dispose();

      sub?.Dispose();
      _changes?.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await Task.Yield();

      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await GetLastUpdate(stoppingToken);
        }
        catch (OperationCanceledException)
        {
          // quiet
        }
        catch (Exception e)
        {
          Log.Error("Error getting data from Oribos: {message}", e.Message);
          await Task.Delay(5000, stoppingToken);
        }
      }
    }

    private async Task GetLastUpdate(CancellationToken ct)
    {
      try
      {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/ORServer.lastupdate.jsp?u={_lastUpdate}");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
          using var responseStream = await response.Content.ReadAsStreamAsync(ct);

          var res = await JsonSerializer.DeserializeAsync<LastUpdateDto>(responseStream,
                                             new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, ct);

          if (res != null && _lastUpdate != res.Update)
          {
            _lastUpdate = res.Update;
            _changes.OnNext(null);
          }

        }
        else
        {
          Log.Error("Error getting data from SportidentCenter (event {event}): response code {code}", _configuration, response.StatusCode);
        }
      }
      catch (OperationCanceledException)
      {
        // quiet
      }
      catch (Exception e)
      {
        Log.Error("Error getting data from SportidentCenter (event {event}): {message}", _configuration, e.Message);
      }
    }

    public async Task OnUpdate(CancellationToken ct)
    {
      Log.Information("Update from Oribos!");

      var request = new HttpRequestMessage(HttpMethod.Get, $"/ORServer.fullweb.jsp");

      var response = await _httpClient.SendAsync(request, ct);

      using var responseStream = await response.Content.ReadAsStreamAsync(ct);

      var res = await JsonSerializer.DeserializeAsync<FullDataDto>(responseStream,
                                         new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }, ct);

      _categoryService.Push(res);
      _resultsService.Push(res);
    }


  }
}

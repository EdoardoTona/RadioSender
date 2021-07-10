using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.ROC
{
  public class ROCService : BackgroundService
  {
    public const string HTTPCLIENT_NAME = "roc";
    private readonly IReadOnlyList<ROCEvent> _events;
    private readonly int _refreshInterval_ms;

    public ROCService(IHttpClientFactory clientFactory, DispatcherService dispatcherService, TimeSpan refreshInterval, IEnumerable<Event> events)
    {
      var httpClient = clientFactory.CreateClient(HTTPCLIENT_NAME);
      _events = events.Select(e => new ROCEvent(httpClient, dispatcherService, e)).ToList();
      _refreshInterval_ms = (int)refreshInterval.TotalMilliseconds;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
      await Task.Yield();

      while (!ct.IsCancellationRequested)
      {
        try
        {
          await Task.WhenAll(_events.Select(e => e.GetData(ct)));
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
          Log.Error("Error getting data from ROC: {message}", e.Message);
        }
        finally
        {
          await Task.Delay(_refreshInterval_ms, ct);
        }
      }
    }

  }
}

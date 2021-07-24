using Hangfire;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Oribos
{
  public class OribosService : ITarget
  {
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly OribosServer _configuration;
    private readonly HttpClient _httpClient;
    private readonly IFilter _filter;
    public OribosService(
      IEnumerable<IFilter> filters,
      IBackgroundJobClient backgroundJobClient,
      IHttpClientFactory httpClientFactory,
      OribosServer server)
    {
      _backgroundJobClient = backgroundJobClient;
      _configuration = server;
      _httpClient = httpClientFactory.CreateClient(_configuration.Host);
      _filter = filters.GetFilter(_configuration.Filter);
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      _backgroundJobClient.Enqueue(() => SendPunchAction(_filter as Filter, _httpClient, punch, default));
      return Task.CompletedTask;
    }

    public Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        SendPunch(punch);

      return Task.CompletedTask;
    }

    public static async Task SendPunchAction(Filter filter, HttpClient httpClient, Punch punch, CancellationToken ct = default)
    {
      punch = filter.Transform(punch);

      if (punch == null)
        return;

      string url = punch.ControlType switch
      {
        PunchControlType.Finish => $"/finish.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}",
        PunchControlType.Start => $"/start.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Clear => $"/clear.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Check => $"/check.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        _ => $"/radiotime.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
      };

      var response = await httpClient.GetAsync(url, ct);

      response.EnsureSuccessStatusCode();

      var text = await response.Content.ReadAsStringAsync(ct);

      if (text.Contains("Ok"))
      {
        text = text.Replace("<html><body><h1>", "").Replace("</h1></body></html>", "");
        string[] r = text.Split(';');
        // r[0] è "Ok"
        if (r.Length > 1)
        {
          var nome = r[1];
          var societa = r[2];
          var nazione = r[3];
          var tempotot = r[4];
        }
      }
      else
      {
        Log.Warning(text);
      }



    }

  }
}

using Hangfire;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Oribos
{
  public class OribosService : ITarget
  {
    private readonly IBackgroundJobClient _backgroundJobClient;
    private static IHttpClientFactory? _httpClientFactory; // static for hangfire
    private OribosServer _configuration;
    private IFilter _filter = Filter.Invariant;
    public OribosService(
      IEnumerable<IFilter> filters,
      IBackgroundJobClient backgroundJobClient,
      IHttpClientFactory httpClientFactory,
      OribosServer configuration)
    {
      _configuration = configuration;
      _backgroundJobClient = backgroundJobClient;
      _httpClientFactory = httpClientFactory;

      UpdateConfiguration(filters, configuration);
    }
    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration!, configuration as OribosServer);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));
    }

    public Task SendPunch(Punch? punch, CancellationToken ct = default)
    {
      if (_filter is Filter f)
        _backgroundJobClient.Enqueue(() => SendPunchAction(f, _configuration, punch, default));
      return Task.CompletedTask;
    }

    public Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        SendPunch(punch, ct);

      return Task.CompletedTask;
    }

    public static async Task SendPunchAction(Filter filter, OribosServer _configuration, Punch? punch, CancellationToken ct = default)
    {
      punch = filter.Transform(punch);

      if (punch == null || string.IsNullOrEmpty(_configuration.Host) || _httpClientFactory == null)
        return;

      var httpClient = _httpClientFactory.CreateClient();

      var host = _configuration.Host.Contains("localhost") ? _configuration.Host.Replace("localhost", "127.0.0.1") : _configuration.Host; // optimization to skip the dns resolution
      httpClient.BaseAddress = new Uri(host);

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
#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
          var nome = r[1];
          var societa = r[2];
          var nazione = r[3];
          var tempotot = r[4];
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore
        }
      }
      else
      {
        Log.Warning(text);
      }



    }

  }


}

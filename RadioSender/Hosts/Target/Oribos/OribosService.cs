using Hangfire;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Oribos;

public class OribosService : ITarget
{
  private readonly IBackgroundJobClient _backgroundJobClient;
  private static IHttpClientFactory? _httpClientFactory; // static for hangfire
  private OribosServer _configuration;
  private readonly FilterService _filterService;

  public OribosService(
    FilterService filterService,
    IBackgroundJobClient backgroundJobClient,
    IHttpClientFactory httpClientFactory,
    OribosServer configuration)
  {
    _filterService = filterService;
    _configuration = configuration;
    _backgroundJobClient = backgroundJobClient;
    _httpClientFactory = httpClientFactory;

  }

  public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
  {
    if (dispatch.Punches == null)
      return Task.CompletedTask;

    var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

    foreach (var punch in punches)
    {
      _backgroundJobClient.Enqueue(() => SendPunchAction(_configuration, punch, default));
    }
    return Task.CompletedTask;
  }

  public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
  {
    foreach (var dispatch in dispatches)
      SendDispatch(dispatch, ct);

    return Task.CompletedTask;
  }

  public static async Task SendPunchAction(OribosServer _configuration, Punch punch, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(_configuration.Host) || _httpClientFactory == null)
      throw new ArgumentException("Missing host");

    using var httpClient = _httpClientFactory.CreateClient();

    var host = _configuration.Host.Contains("localhost") ? _configuration.Host.Replace("localhost", "127.0.0.1") : _configuration.Host; // optimization to skip the dns resolution
    httpClient.BaseAddress = new Uri(host);

    string? url;
    if (punch.CompetitorIdType == CompetitorIdType.PunchingCard)
    {
      if (punch.NetTime)
      {
        Log.Warning("Net time not supported in Oribos with Sportident card numbers. Ignored");
        return;
      }

      url = punch.ControlType switch
      {
        PunchControlType.Finish => $"/finish.html?card={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}",
        PunchControlType.Start => $"/start.html?card={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Clear => $"/clear.html?card={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Check => $"/check.html?card={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        _ => $"/radiotime.html?card={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
      };
    }
    else if (punch.CompetitorIdType == CompetitorIdType.BibNumber)
    {

      if (punch.NetTime)
      {
        url = punch.ControlType switch
        {
          PunchControlType.Finish => $"/algetime.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}",
          PunchControlType.Control => $"/algetime.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
          PunchControlType.Unknown => $"/algetime.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
          _ => null
        };

        if (url == null)
        {
          Log.Warning("Net time not supported in Oribos if control type is not finish or control. Ignored");
          return;
        }

      }
      else
      {
        if (punch.CompetitorStatus != CompetitorStatus.Unknown)
        {
          url = punch.CompetitorStatus switch
          {
            CompetitorStatus.DNS => $"/changestate.html?pett={punch.CompetitorId}&state=np",
            CompetitorStatus.Running => $"/changestate.html?pett={punch.CompetitorId}&state=ga",
            CompetitorStatus.WaitingStart => $"/changestate.html?pett={punch.CompetitorId}&state=ip",
            _ => null
          };
        }
        else if (punch.Cancellation)
        {
          url = punch.ControlType switch
          {
            PunchControlType.Finish => $"/cronofinish.html?pett={punch.CompetitorId}&time=00.00.00&type=1&abs=0",
            PunchControlType.Start => $"/cronostart.html?pett={punch.CompetitorId}&time=00.00.00&type=1&abs=0",
            _ => null
          };
        }
        else
        {
          url = punch.ControlType switch
          {
            PunchControlType.Finish => $"/cronofinish.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&abs=1",
            PunchControlType.Start => $"/cronostart.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&abs=1",
            PunchControlType.Control => $"/cronoradio.html?pett={punch.CompetitorId}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}&abs=1",
            _ => null
          };
        }

      }

    }
    else
    {
      Log.Warning("Oribos cannot choose card or bib endpoint for competitor id type {type}. Ignored", punch.CompetitorIdType);
      return;
    }

    if (string.IsNullOrEmpty(url))
    {
      Log.Warning("The event cannot be forwarded to Oribos");
      return;
    }

    HttpResponseMessage response;
    try
    {
      response = await httpClient.GetAsync(url, ct);
    }
    catch
    {
      Log.Warning("Oribos not reachable: {host}", host);
      throw;
    }

    try
    {
      response.EnsureSuccessStatusCode();
    }
    catch
    {
      Log.Warning("Oribos responded: {status}", response.StatusCode);
      throw;
    }

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

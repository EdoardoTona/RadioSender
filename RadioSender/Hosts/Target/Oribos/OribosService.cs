using RadioSender.Hosts.Common;
using Serilog;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Oribos
{
  public class OribosService
  {
    public const string HTTPCLIENT_NAME = "oribos";
    private readonly HttpClient _httpClient;
    public OribosService(IHttpClientFactory httpClientFactory)
    {
      _httpClient = httpClientFactory.CreateClient(HTTPCLIENT_NAME);
    }

    public PunchControlType SafeType(Punch punch)
    {
      if (punch.ControlType == PunchControlType.Unknown)
      {
        if (punch.Control == 999 || (punch.Control >= 25 && punch.Control <= 30) || (punch.Control >= 2 && punch.Control <= 10))
        {
          // 999 for OE2010
          // 10 for OLA
          // 2-9 are unknown... fallback on finish
          return PunchControlType.Finish;
        }
        else if (punch.Control >= 21 && punch.Control <= 24)
        {
          return PunchControlType.Start;
        }
        else if (punch.Control == 1 || (punch.Control >= 16 && punch.Control <= 20))
        {
          // 1 is suggested by Sportident to avoid flashing on card (model 11, SIAC)
          return PunchControlType.Clear;
        }
        else if (punch.Control >= 11 && punch.Control <= 15)
        {
          return PunchControlType.Check;
        }
        else
          return PunchControlType.Control;
      }
      else
      {
        return punch.ControlType;
      }
    }

    public async Task SendPunch(Punch punch, CancellationToken ct)
    {
      if (punch == null)
        return;

      string url;
      switch (SafeType(punch))
      {
        case PunchControlType.Finish:
          url = $"/finish.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}";
          break;
        case PunchControlType.Start:
          url = $"/start.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}";
          break;
        case PunchControlType.Clear:
          url = $"/clear.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}";
          break;
        case PunchControlType.Check:
          url = $"/check.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}";
          break;
        default:
          url = $"/radiotime.html?card={punch.Card}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}";
          break;
      }

      var response = await _httpClient.GetAsync(url, ct);

      var text = await response.Content.ReadAsStringAsync();

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

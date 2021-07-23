using RadioSender.Hosts.Common;
using Serilog;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Oribos
{
  public class OribosService : ITarget
  {
    private readonly OribosServer _server;
    private readonly HttpClient _httpClient;
    public OribosService(IHttpClientFactory httpClientFactory, OribosServer server)
    {
      _server = server;
      _httpClient = httpClientFactory.CreateClient(_server.Host);
    }

    public async Task SendPunch(Punch punch, CancellationToken ct = default)
    {
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

      var response = await _httpClient.GetAsync(url, ct);

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

using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Target.Oribos;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Liveresults.Oribos
{
  public class OribosService : IHostedService
  {
    private readonly HttpClient _httpClient;
    private readonly OribosServer _configuration;
    public OribosService(
      IHttpClientFactory httpClientFactory,
      OribosServer configuration
      )
    {
      _httpClient = httpClientFactory.CreateClient();
      _configuration = configuration;

      var host = _configuration.Host.Contains("localhost") ? _configuration.Host.Replace("localhost", "127.0.0.1") : _configuration.Host; // optimization to skip the dns resolution
      _httpClient.BaseAddress = new Uri(host);

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      throw new NotImplementedException();
    }
  }
}

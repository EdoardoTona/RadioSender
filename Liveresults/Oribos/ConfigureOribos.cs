using Liveresults.Oribos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace RadioSender.Hosts.Target.Oribos
{
  public record OribosServer
  {
    public string Host { get; init; }
  }

  public static class ConfigureOribos
  {
    public static IHostBuilder ToOribos(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        var server = context.Configuration.GetSection("Target:Oribos").Get<OribosServer>();

        services.AddSingleton(s => new OribosService(
          s.GetRequiredService<IHttpClientFactory>(),
          server));

      });

      return builder;
    }
  }
}

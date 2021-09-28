using Liveresults.Oribos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace Liveresults.Oribos
{
  public record OribosServer
  {
    public string Host { get; init; }
  }

  public static class ConfigureOribos
  {
    public static IHostBuilder FromOribos(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        var server = context.Configuration.GetSection("Oribos").Get<OribosServer>();

        services.AddHttpClient();

        services.AddSingleton<CategoryService>();
        services.AddSingleton<ResultsService>();

        services.AddHostedService(s => new OribosService(
          s.GetRequiredService<IHttpClientFactory>(),
          server,

          s.GetRequiredService<CategoryService>(),

          s.GetRequiredService<ResultsService>()));

      });

      return builder;
    }
  }
}

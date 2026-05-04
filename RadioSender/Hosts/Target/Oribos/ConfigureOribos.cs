using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace RadioSender.Hosts.Target.Oribos
{
  public record OribosServer : FilterableConfiguration
  {
    public string? Host { get; init; }
    [System.Obsolete("Oribos now chooses the endpoint from Punch.CompetitorIdType.")]
    public bool UseStartNumbers { get; init; }
  }

  public static class ConfigureOribos
  {
    public static IHostBuilder ToOribos(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:Oribos:Enable", false))
          return;

        var servers = context.Configuration.GetSection("Target:Oribos:Servers").Get<IEnumerable<OribosServer>>();

        if (servers!.Any())
          services.AddHttpClient();

        if (servers != null)
          foreach (var server in servers)
          {
            services.AddSingleton<ITarget>(s => new OribosService(
              s.GetRequiredService<FilterService>(),
              s.GetRequiredService<IBackgroundJobClient>(),
              s.GetRequiredService<IHttpClientFactory>(),
              server));
          }

      });

      return builder;
    }
  }
}

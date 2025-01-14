using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.SIRAP
{
  public record SirapServerConfiguration : FilterableConfiguration
  {
    public int? Port { get; init; }
  }

  public static class ConfigureSirapServer
  {
    public static IHostBuilder FromSirap(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SIRAP:Enable", false))
          return;

        var servers = context.Configuration.GetSection("Source:SIRAP:Servers").Get<IEnumerable<SirapServerConfiguration>>();
        if (servers == null) return;

        foreach (var server in servers)
        {
          services.AddHostedService(sp =>
            new SirapServer(
              sp.GetServices<IFilter>(),
              sp.GetRequiredService<DispatcherService>(),
              server
              )
          );
        }

      });

      return builder;
    }
  }
}

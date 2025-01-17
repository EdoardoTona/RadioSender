using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.Microplus;

public record MicroplusServerConfiguration : FilterableConfiguration
{
  public int? Port { get; init; }
  public IEnumerable<string>? IgnoreCommands { get; set; }
}

public static class ConfigureMicroplusServer
{
  public static IHostBuilder FromMicroplus(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Source:Microplus:Enable", false))
        return;

      var servers = context.Configuration.GetSection("Source:Microplus:Servers").Get<IEnumerable<MicroplusServerConfiguration>>();
      if (servers == null)
        return;

      foreach (var server in servers)
      {
        services.AddHostedService(sp =>
          new MicroplusServer(
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

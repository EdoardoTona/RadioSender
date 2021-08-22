using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Target.SIRAP
{
  public record SirapClientConfiguration : FilterableConfiguration
  {
    public string? Address { get; init; }
    public int? Port { get; init; }
    public int Version { get; init; } = 2;
    public TimeSpan ZeroTime { get; init; } = TimeSpan.Zero;
  }

  public static class ConfigureSirapClient
  {
    public static IHostBuilder ToSirap(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:SIRAP:Enable", false))
          return;

        var clients = context.Configuration.GetSection("Target:SIRAP:Clients").Get<IEnumerable<SirapClientConfiguration>>();

        foreach (var client in clients)
        {
          services.AddSingleton<ITarget>(sp =>
            new SirapClient(
              sp.GetServices<IFilter>(),
              client
              )
          );
        }

      });

      return builder;
    }
  }
}

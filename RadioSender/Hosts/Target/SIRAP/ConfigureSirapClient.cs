using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Target.SIRAP
{
  public record SirapClientConfiguration : FilterableConfiguration
  {
    public string Address { get; init; }
    public int Port { get; init; }
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

        foreach (var c in clients)
        {
          var client = c;
          if (c.Address.ToLower().Equals("localhost"))
            client = c with { Address = client.Address.Replace("localhost", "127.0.0.1") }; // optimization to skip the dns resolution

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

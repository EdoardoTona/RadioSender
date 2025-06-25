using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Hosts.Target.Tcp
{
  public record TcpTargetConfiguration : FilterableConfiguration
  {
    public string? Address { get; init; }
    public int? Port { get; init; }
    public string? Format { get; init; }
    public bool AsServer { get; init; }
  }

  public static class ConfigureTcpTarget
  {
    public static IHostBuilder ToTcp(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:Tcp:Enable", false))
          return;

        var targets = context.Configuration.GetSection("Target:Tcp:Targets").Get<IEnumerable<TcpTargetConfiguration>>();
        if (targets == null) return;

        foreach (var target in targets)
        {
          if (target.AsServer)
          {
            services.AddSingleton<ITarget>(sp => new TcpTargetServer(
                sp.GetRequiredService<FilterService>(),
                target
                ));

            services.AddSingleton<IRadioSenderHost, TcpTargetServer>(sp =>
            (TcpTargetServer)sp.GetServices<ITarget>().First(t => (t as TcpTargetServer)?.GetConfiguration() == target));

          }
          else
          {
            services.AddSingleton<ITarget>(sp =>
              new TcpTargetClient(
                sp.GetRequiredService<FilterService>(),
                target
                )
            );
          }
        }

      });

      return builder;
    }
  }
}

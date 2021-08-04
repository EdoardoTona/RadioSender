using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Hosts.Target.Tcp
{
  public record TcpTargetConfiguration : FilterableConfiguration
  {
    public string Address { get; init; }
    public int Port { get; init; }
    public string Format { get; init; }
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

        foreach (var t in targets)
        {
          var target = t;
          if (t.Address.ToLower().Equals("localhost"))
            target = t with { Address = target.Address.Replace("localhost", "127.0.0.1") }; // optimization to skip the dns resolution

          if (target.AsServer)
          {
            services.AddSingleton<ITarget>(sp => new TcpTargetServer(
                sp.GetServices<IFilter>(),
                target
                ));

            services.AddHostedService(sp => (TcpTargetServer)sp.GetServices<ITarget>().Where(t => t is TcpTargetServer).First()); // TODO

          }
          else
          {
            services.AddSingleton<ITarget>(sp =>
              new TcpTargetClient(
                sp.GetServices<IFilter>(),
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

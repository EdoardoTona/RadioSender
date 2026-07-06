using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.Tcp
{
  public record TcpSourceConfiguration : FilterableConfiguration
  {
    public string? Address { get; init; }
    public int? Port { get; init; }
    // Same template syntax as the Tcp target, parsed in reverse.
    // Default expects: {CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}
    public string? Format { get; init; }
    // false (default) = connect out to Address:Port as a client;
    // true = listen on Port and accept incoming connections.
    public bool AsServer { get; init; }
    public string? SourceId { get; init; }
  }

  public static class ConfigureTcpSource
  {
    public const string DefaultFormat = "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}";

    public static IHostBuilder FromTcp(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:Tcp:Enable", false))
          return;

        var servers = context.Configuration.GetSection("Source:Tcp:Servers").Get<IEnumerable<TcpSourceConfiguration>>();
        if (servers == null) return;

        foreach (var server in servers)
        {
          var cfg = server;
          if (string.IsNullOrWhiteSpace(cfg.Format))
            cfg = cfg with { Format = DefaultFormat };

          if (cfg.AsServer)
          {
            services.AddSingleton<IRadioSenderHost, TcpSourceServer>(sp => new TcpSourceServer(
                sp.GetRequiredService<FilterService>(),
                sp.GetRequiredService<DispatcherService>(),
                cfg
                ));
          }
          else
          {
            services.AddSingleton<IRadioSenderHost, TcpSourceClient>(sp => new TcpSourceClient(
                sp.GetRequiredService<FilterService>(),
                sp.GetRequiredService<DispatcherService>(),
                cfg
                ));
          }
        }
      });

      return builder;
    }
  }
}

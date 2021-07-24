using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Target.Oribos
{
  public record OribosServer : FilterableConfiguration
  {
    public string Host { get; init; }
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

        foreach (var s in servers)
        {
          var server = s;
          if (s.Host.Contains("localhost"))
            server = s with { Host = server.Host.Replace("localhost", "127.0.0.1") }; // optimization to skip the dns resolution

          services.AddHttpClient(server.Host, c => { c.BaseAddress = new Uri(server.Host); });
          services.AddSingleton<ITarget>(s => new OribosService(
            s.GetServices<IFilter>(),
            s.GetRequiredService<IBackgroundJobClient>(),
            s.GetRequiredService<IHttpClientFactory>(),
            server));
        }

      });

      return builder;
    }
  }
}

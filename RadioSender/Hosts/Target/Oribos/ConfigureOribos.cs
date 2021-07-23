using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Target.Oribos
{
  public record OribosServer(string Host);

  public static class ConfigureOribos
  {
    public static IHostBuilder UseOribos(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:Oribos:Enable", false))
          return;

        var servers = context.Configuration.GetSection("Target:Oribos:Servers").Get<IEnumerable<OribosServer>>();

        foreach (var server in servers)
        {
          var host = context.Configuration.GetValue<string>("Target:Oribos:Host");
          host = host.Replace("localhost", "127.0.0.1"); // optimization to skip the dns resolution

          services.AddHttpClient(host, c => { c.BaseAddress = new Uri(host); });
          services.AddSingleton(s => new OribosService(s.GetRequiredService<IHttpClientFactory>(), server));
        }

      });

      return builder;
    }
  }
}

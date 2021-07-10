using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System;

namespace RadioSender.Hosts.Target.Oribos
{
  public static class ConfigureOribos
  {
    public static IHostBuilder UseOribos(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:Oribos:Enable", false))
          return;

        var host = context.Configuration.GetValue<string>("Target:Oribos:Host");
        host = host.Replace("localhost", "127.0.0.1"); // skip dns resolution

        services.AddHttpClient(OribosService.HTTPCLIENT_NAME, c =>
        {
          c.BaseAddress = new Uri(host);
        });
        services.AddSingleton<OribosService>();

      });

      return builder;
    }
  }
}

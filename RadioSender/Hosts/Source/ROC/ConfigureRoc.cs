using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Source.ROC
{
  public record Event
  {
    public int EventId { get; set; }
    public TimeSpan IgnoreOlderThan { get; set; }
    public int RefreshMs { get; set; } = 1000;
  }

  public static class ConfigureRoc
  {
    public static IHostBuilder UseRoc(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:ROC:Enable", false))
          return;

        var events = context.Configuration.GetSection("Source:ROC:Events").Get<IEnumerable<Event>>();

        services.AddHttpClient(ROCEvent.HTTPCLIENT_NAME, c =>
        {
          c.BaseAddress = new Uri("https://roc.olresultat.se/");
        });

        foreach (var ev in events)
        {
          services.AddHostedService(sp =>
            new ROCEvent(
              sp.GetRequiredService<IHttpClientFactory>(),
              sp.GetRequiredService<DispatcherService>(),
              ev
              )
          );
        }

      });

      return builder;
    }
  }
}

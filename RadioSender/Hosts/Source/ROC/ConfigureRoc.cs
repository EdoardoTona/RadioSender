using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Source.ROC
{
  public record Event : FilterableConfiguration
  {
    public int? EventId { get; init; }
    public int RefreshMs { get; init; } = 1000;
    public string Host { get; init; } = "https://roc.olresultat.se/";
  }

  public static class ConfigureRoc
  {
    public static IHostBuilder FromRoc(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:ROC:Enable", false))
          return;

        var events = context.Configuration.GetSection("Source:ROC:Events").Get<IEnumerable<Event>>();

        foreach (var ev in events)
        {
          services.AddHttpClient(ROCEvent.HTTPCLIENT_NAME, c => // TODO if added twice
          {
            c.BaseAddress = new Uri(ev.Host);
          });

          services.AddHostedService(sp =>
            new ROCEvent(
              sp.GetServices<IFilter>(),
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

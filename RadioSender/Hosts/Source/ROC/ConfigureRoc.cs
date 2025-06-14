using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    public bool Enable { get; init; } = true;
    public int RefreshMs { get; init; } = 2000;
    public string Host { get; init; } = "https://roc.olresultat.se/";
    public string Path { get; init; } = "/getpunches.asp?unitId={EventId}&lastId={LastId}";
  }

  public static class ConfigureRoc
  {
    public static IHostBuilder FromRoc(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:ROC:Enable", false))
          return;

        var events = context.Configuration.GetSection("Source:ROC:Events").Get<List<Event>>();

        if (events != null)
          foreach (var ev in events)
          {
            services.AddHttpClient(ROCEvent.HTTPCLIENT_NAME + ev.EventId, c =>
            {
              c.BaseAddress = new Uri(ev.Host);
            });

            services.AddSingleton<IHostedService, ROCEvent>(sp => new ROCEvent(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<DispatcherService>(),
                sp.GetRequiredService<FilterService>(),
                ev,
                ev.EventId ?? 0 // Passa l'EventId per identificare la configurazione
                )
            );
          }

      });

      return builder;
    }
  }
}

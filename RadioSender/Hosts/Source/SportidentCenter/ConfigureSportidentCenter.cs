using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Source.SportidentCenter
{
  public record Event : FilterableConfiguration
  {
    public int? EventId { get; init; }
    public string? ApiKey { get; init; }
    public int RefreshMs { get; init; } = 1000;
  }

  public static class ConfigureSportidentCenter
  {
    public static IHostBuilder FromSportidentCenter(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SportidentCenter:Enable", false))
          return;

        services.AddHttpClient(SportidentCenterEvent.HTTPCLIENT_NAME, c =>
        {
          c.BaseAddress = new Uri("https://center.sportident.com/");
        });

        var events = context.Configuration.GetSection("Source:SportidentCenter:Events").Get<IEnumerable<Event>>();

        foreach (var ev in events)
        {
          services.AddHostedService(sp =>
           new SportidentCenterEvent(
             sp.GetServices<IFilter>(),
             sp.GetRequiredService<IHttpClientFactory>(),
             sp.GetRequiredService<DispatcherService>(),
             ev)
         );
        }

      });

      return builder;
    }
  }
}

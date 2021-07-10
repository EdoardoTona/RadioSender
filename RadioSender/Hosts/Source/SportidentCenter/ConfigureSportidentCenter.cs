using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Source.SportidentCenter
{
  public record Event
  {
    public int EventId { get; set; }
    public string ApiKey { get; set; }
    public TimeSpan IgnoreOlderThan { get; set; }
  }

  public static class ConfigureSportidentCenter
  {
    public static IHostBuilder UseSportidentCenter(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SportidentCenter:Enable", false))
          return;

        var events = context.Configuration.GetSection("Source:SportidentCenter:Events").Get<IEnumerable<Event>>();

        services.AddHttpClient(SportidentCenterService.HTTPCLIENT_NAME, c =>
        {
          c.BaseAddress = new Uri("https://center.sportident.com/");
        });
        services.AddHostedService(sp =>
          new SportidentCenterService(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<DispatcherService>(),
            context.Configuration.GetValue("Source:SportidentCenter:Refresh", TimeSpan.FromSeconds(5)),
            events
            )
        );
      });

      return builder;
    }
  }
}

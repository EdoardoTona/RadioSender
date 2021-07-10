using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.ROC
{
  public record Event
  {
    public int EventId { get; set; }
    public TimeSpan IgnoreOlderThan { get; set; }
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

        services.AddHttpClient(ROCService.HTTPCLIENT_NAME, c =>
        {
          c.BaseAddress = new Uri("https://roc.olresultat.se/");
        });
        services.AddHostedService(sp =>
          new ROCService(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<DispatcherService>(),
            context.Configuration.GetValue("Source:ROC:Refresh", TimeSpan.FromSeconds(5)),
            events
            )
        );
      });

      return builder;
    }
  }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.SIRAP
{
  public record Listner
  {
    public int Port { get; set; }
    public int Version { get; set; }
  }

  public static class ConfigureSIRAP
  {
    public static IHostBuilder UseSirap(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SIRAP:Enable", false))
          return;

        var listners = context.Configuration.GetSection("Source:SIRAP:Listners").Get<IEnumerable<Listner>>();

        //services.AddHostedService(sp =>
        //  new ROCService(
        //    sp.GetRequiredService<IHttpClientFactory>(),
        //    sp.GetRequiredService<DispatcherService>(),
        //    context.Configuration.GetValue("Source:ROC:Refresh", TimeSpan.FromSeconds(5)),
        //    events
        //    )
        //);
      });

      return builder;
    }
  }
}

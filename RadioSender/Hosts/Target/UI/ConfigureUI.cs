using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hubs;

namespace RadioSender.Hosts.Target.UI
{
  public record UIConfiguration : FilterableConfiguration
  {
    public bool Enable { get; init; }
  }
  public static class ConfigureUI
  {
    public static IHostBuilder ToUI(this IHostBuilder builder)
    {
      builder
        .ConfigureServices((context, services) =>
        {
          services.AddHostedService<LogService>();
          services.AddHostedService<StatsService>();

          var conf = context.Configuration.GetSection("Target:UI").Get<UIConfiguration>();
          if (conf == null || !conf.Enable)
            return;

          services.AddSingleton(sp => new UIService(
            sp.GetServices<IFilter>(),
            sp.GetRequiredService<IHubContext<DeviceHub, IDeviceHub>>(),
            sp.GetRequiredService<HubEvents>(),
            conf));

          services.AddSingleton<ITarget>(sp => sp.GetRequiredService<UIService>());

          services.AddHostedService(sp => sp.GetRequiredService<UIService>());

        })
        .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

      return builder;
    }
  }
}

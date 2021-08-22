using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;

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
          var conf = context.Configuration.GetSection("Target:UI").Get<UIConfiguration>();
          if (conf == null || !conf.Enable)
            return;

          services.AddSingleton<ITarget>(sp => new UIService(sp.GetServices<IFilter>(), conf));
          services.AddHostedService<Launcher>();
        })
        .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

      return builder;

    }
  }
}

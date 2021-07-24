using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RadioSender.Hosts.Target.UI
{
  public static class ConfigureUI
  {
    public static IHostBuilder ToUI(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:UI:Enable", false))
          return;

        services.AddSingleton<ITarget, UIService>();
      });

      return builder;
    }
  }
}

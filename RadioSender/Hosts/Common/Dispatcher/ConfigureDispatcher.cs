using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;

namespace RadioSender.Hosts.Common
{
  public static class ConfigureDispatcher
  {
    public static IHostBuilder UseDispatcher(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        services.AddSingleton<DispatcherService>();
      });

      return builder;
    }
  }
}

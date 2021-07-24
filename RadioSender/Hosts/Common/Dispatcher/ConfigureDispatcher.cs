using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;

namespace RadioSender.Hosts.Common
{
  public record DispatcherConfiguration : FilterableConfiguration
  {
  }
  public static class ConfigureDispatcher
  {
    public static IHostBuilder ThroughDispatcher(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        var conf = context.Configuration.GetSection("Dispatcher").Get<DispatcherConfiguration>();

        services.AddSingleton(sp => new DispatcherService(sp.GetServices<IFilter>(), sp.GetServices<ITarget>(), conf));
      });

      return builder;
    }
  }
}

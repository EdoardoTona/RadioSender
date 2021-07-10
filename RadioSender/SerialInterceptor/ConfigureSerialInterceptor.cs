using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RadioSender.SerialInterceptor
{
  public static class ConfigureSerialInterceptor
  {
    public static IHostBuilder UseInterceptor(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        services.AddHostedService<SerialInterceptor>();
      });

      return builder;
    }
  }
}
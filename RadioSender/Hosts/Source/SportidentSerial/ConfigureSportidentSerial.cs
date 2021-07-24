using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.SportidentSerial
{
  public record Port : FilterableConfiguration
  {
    public string PortName { get; init; }
    public int Baudrate { get; init; } = 38400;
  }

  public static class ConfigureSportidentSerial
  {
    public static IHostBuilder FromSportidentSerial(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SportidentSerial:Enable", false))
          return;

        var ports = context.Configuration.GetSection("Source:SportidentSerial:Ports").Get<IEnumerable<Port>>();

        foreach (var port in ports)
        {
          services.AddHostedService(sp => new SportidentSerialPort(
            sp.GetServices<IFilter>(),
            sp.GetRequiredService<DispatcherService>(),
            port));
        }

      });

      return builder;
    }
  }
}

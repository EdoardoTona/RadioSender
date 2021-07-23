using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Hosts.Source.SportidentSerial
{
  public record Port
  {
    public string PortName { get; set; }
    public int Baudrate { get; set; } = 38400;
  }

  public static class ConfigureSportidentSerial
  {
    public static IHostBuilder UseSportidentSerial(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:SportidentSerial:Enable", false))
          return;

        var ports = context.Configuration.GetSection("Source:SportidentSerial:Ports").Get<IEnumerable<Port>>();

        foreach (var port in ports)
        {
          services.AddHostedService(sp => new SportidentSerialPort(sp.GetRequiredService<DispatcherService>(), port));
        }

      });

      return builder;
    }
  }
}

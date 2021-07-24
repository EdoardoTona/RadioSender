using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hubs.Devices;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record Gateway
  {
    public string PortName { get; set; }
    public int Baudrate { get; set; } = 19200;
  }

  public static class ConfigureTmFRadio
  {
    public static IHostBuilder UseTmFRadio(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:TmFRadio:Enable", false))
          return;

        foreach (var gateway in context.Configuration.GetSection("Source:TmFRadio:Gateways").Get<IEnumerable<Gateway>>())
        {
          services.AddHostedService(sp => new TmFRadioGateway(sp.GetRequiredService<DispatcherService>(), sp.GetRequiredService<DeviceService>(), gateway));
        }

      });

      return builder;
    }
  }
}

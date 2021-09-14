using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record Gateway : FilterableConfiguration
  {
    public string? PortName { get; init; }
    public int Baudrate { get; init; } = 19200;
    public int StatusCheck { get; init; } = 10; // seconds
  }

  public static class ConfigureTmFRadio
  {
    public static IHostBuilder FromTmFRadio(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Source:TmFRadio:Enable", false))
          return;

        foreach (var gateway in context.Configuration.GetSection("Source:TmFRadio:Gateways").Get<IEnumerable<Gateway>>())
        {
          services.AddHostedService(sp => new TmFRadioGateway(
            sp.GetServices<IFilter>(),
            sp.GetRequiredService<DispatcherService>(),
            gateway));
        }

      });

      return builder;
    }
  }
}

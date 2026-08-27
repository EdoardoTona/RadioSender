using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.Microplus;

public record MicrogateSourceConfiguration : FilterableConfiguration
{
  public string? Address { get; init; }
  public int? Port { get; init; }
  public string? PortName { get; init; }
  public int Baudrate { get; init; } = 115200;
  public bool DtrEnable { get; init; }
  public bool RtsEnable { get; init; }
}

public static class ConfigureMicrogateSource
{
  public static IHostBuilder FromMicrogate(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Source:Microgate:Enable", false))
        return;

      var sources = context.Configuration.GetSection("Source:Microgate:Sources").Get<IEnumerable<MicrogateSourceConfiguration>>();
      if (sources == null)
        return;

      foreach (var source in sources)
      {
        if (string.IsNullOrWhiteSpace(source.PortName))
        {
          services.AddSingleton<IRadioSenderHost>(sp => new MicrogateTcpSource(
            sp.GetRequiredService<FilterService>(),
            sp.GetRequiredService<DispatcherService>(),
            source));
        }
        else
        {
          services.AddSingleton<IRadioSenderHost>(sp => new MicrogateSerialSource(
            sp.GetRequiredService<FilterService>(),
            sp.GetRequiredService<DispatcherService>(),
            source));
        }
      }

    });

    return builder;
  }
}

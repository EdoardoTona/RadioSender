using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.OBR;

public record ObrSourceConfiguration : FilterableConfiguration
{
  public int? Port { get; init; }
  public string? AllowedIp { get; init; }
  public string? SourceId { get; init; }
}

public static class ConfigureObrSource
{
  public static IHostBuilder FromObr(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Source:OBR:Enable", false))
        return;

      var sources = context.Configuration.GetSection("Source:OBR:Sources").Get<IEnumerable<ObrSourceConfiguration>>();
      if (sources == null) return;

      foreach (var source in sources)
      {
        services.AddSingleton<IRadioSenderHost, ObrSource>(sp => new ObrSource(
            sp.GetRequiredService<FilterService>(),
            sp.GetRequiredService<DispatcherService>(),
            source
            )
        );
      }
    });

    return builder;
  }
}

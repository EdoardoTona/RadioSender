using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Target.PosPrinter;

public record PrinterTargetConfiguration : FilterableConfiguration
{
  public string? PrinterName { get; set; }
  public string? Format { get; set; }
  public int[]? ColumnWidths { get; set; }
}

public static class ConfigurePrinterTarget
{
  public static IHostBuilder ToPrinter(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Target:Printer:Enable", false))
        return;

      var printers = context.Configuration.GetSection("Target:Printer:Printers").Get<IEnumerable<PrinterTargetConfiguration>>();

      if (printers != null)
        foreach (var printer in printers)
        {
          services.AddSingleton<ITarget>(sp =>
                  new PrinterTarget(
                      sp.GetRequiredService<FilterService>(),
                      printer,
                      sp.GetRequiredService<IHostApplicationLifetime>()
                  )
              );
        }
    });

    return builder;
  }
}

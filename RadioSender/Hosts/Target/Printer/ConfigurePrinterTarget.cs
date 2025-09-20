using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace RadioSender.Hosts.Target.PosPrinter;

public record PrinterTargetConfiguration : FilterableConfiguration
{
  public string? PrinterName { get; set; }
  public string? Format { get; set; }
}

public static class ConfigurePrinterTarget
{
  [SupportedOSPlatform("windows")]
  public static IHostBuilder ToPrinter(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Target:Printer:Enable", false))
        return;

      // Log delle stampanti disponibili per debug
      LogAvailablePrinters();

      var printers = context.Configuration.GetSection("Target:Printer:Printers").Get<IEnumerable<PrinterTargetConfiguration>>();

      if (printers != null)
        foreach (var printer in printers)
        {
          services.AddSingleton<ITarget>(sp =>
                  new PrinterTarget(
                      sp.GetRequiredService<FilterService>(),
                      printer
                  )
              );
        }
    });

    return builder;
  }

  [SupportedOSPlatform("windows")]
  private static void LogAvailablePrinters()
  {
    try
    {
      // Per ESC/POS, log solo un messaggio informativo
      // La verifica delle stampanti sarà fatta durante l'inizializzazione
      Serilog.Log.Information("ESC/POS Printer target initialized. Printer validation will occur during first print attempt.");
    }
    catch (Exception ex)
    {
      Serilog.Log.Error(ex, "Failed to initialize ESC/POS printer logging");
    }
  }
}
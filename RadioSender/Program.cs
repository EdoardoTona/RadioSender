using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Source.ROC;
using RadioSender.Hosts.Source.SIRAP;
using RadioSender.Hosts.Source.SportidentCenter;
using RadioSender.Hosts.Source.SportidentSerial;
using RadioSender.Hosts.Source.TmFRadio;
using RadioSender.Hosts.Target.File;
using RadioSender.Hosts.Target.Oribos;
using RadioSender.Hosts.Target.UI;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;

namespace RadioSender
{
  public class Program
  {
    public static int Main(string[] args)
    {
      var configuration = new ConfigurationBuilder()
          .SetBasePath(Directory.GetCurrentDirectory())
          .AddJsonFile("appsettings.json")
          .Build();

      Log.Logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(configuration)
                        .Enrich.FromLogContext()
                        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                        .CreateLogger();
      try
      {
        Log.Information("**** Starting up {application} {version} ****", Assembly.GetExecutingAssembly().GetName().Name, Assembly.GetExecutingAssembly().GetName().Version);
        CreateHostBuilder(args).Build().Run();
        Log.Information("**** Shutting down ****");
        return 0;
      }
      catch (OperationCanceledException)
      {
        return 0;
      }
      catch (Exception e)
      {
        Log.Error(e, "**** Main Exception ****");
        return 1;
      }
      finally
      {
        Log.CloseAndFlush();
      }

    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseHangfire()
            .UseFilters()

            // Sources
            .FromRoc()
            .FromSportidentCenter()
            .FromSportidentSerial()
            .FromTmFRadio()
            .FromSirap()

            // Middleware
            .ThroughDispatcher()

            // Targets
            .ToOribos()
            .ToUI()
            .ToFile();

  }
}

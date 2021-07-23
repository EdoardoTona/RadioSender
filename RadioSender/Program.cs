using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Source.ROC;
using RadioSender.Hosts.Source.SportidentCenter;
using RadioSender.Hosts.Source.SportidentSerial;
using RadioSender.Hosts.Source.TmFRadio;
using RadioSender.Hosts.Target.Oribos;
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
            .UseOribos()
            .UseDispatcher()
            .UseRoc()
            .UseSportidentCenter()
            .UseSportidentSerial()
            .UseTmFRadio()
            .ConfigureServices(services => services.AddHostedService<Launcher>())
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

  }
}

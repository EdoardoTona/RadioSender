using CliWrap;
using Common;
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
using RadioSender.Hosts.Target.Http;
using RadioSender.Hosts.Target.Oribos;
using RadioSender.Hosts.Target.SIRAP;
using RadioSender.Hosts.Target.Tcp;
using RadioSender.Hosts.Target.UI;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RadioSender
{
  public static class Program
  {
    public static int Main(string[] args)
    {
      try
      {
        var appsettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (!File.Exists(appsettings))
          throw new FileNotFoundException("Configuration file not found at " + appsettings);

        var configuration = new ConfigurationBuilder()
                                .SetBasePath(Directory.GetCurrentDirectory())
                                .AddJsonFile(appsettings, optional: true)
                                .Build();

        Log.Logger = new LoggerConfiguration()
                      .ReadFrom.Configuration(configuration)
                      .Enrich.FromLogContext()
                      .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                      .WriteTo.EventLogSink()
                      .CreateLogger();

        var assembly = Assembly.GetExecutingAssembly().GetName();

        Log.Information("**** Starting up {application} {version} ****", assembly.Name, assembly.Version);

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
        PopupException(e);
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

            .ActivatePhotino()

            // Sources
            .FromRoc()
            .FromSportidentCenter()
            .FromSportidentSerial()
            .FromTmFRadio()
            .FromSirap()

            // Middleware
            .ThroughDispatcher()

            // Targets
            .ToUI()
            .ToOribos()
            .ToFile()
            .ToSirap()
            .ToTcp()
            .ToHttp();

    public static void PopupException(Exception e)
    {
      Log.Error(e, "**** Main Exception ****");

      try
      {
        var message = e.Message.Replace("'", "\"") +
                      Environment.NewLine +
                      Environment.NewLine +
                      e.GetType().ToString() +
                      Environment.NewLine +
                      e.StackTrace?.Replace("'", "\"");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
          Cli.Wrap("powershell")
             .WithArguments(
                "Add-Type -AssemblyName PresentationCore,PresentationFramework; " +
                "[System.Windows.MessageBox]::Show('" + message + "','Radiosender','Ok','Error')")
             .ExecuteAsync()
             .Task.Wait();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
          // TODO test
          Cli.Wrap("bash")
             .WithArguments(
                "osascript -e 'tell app \"Finder\" to display dialog \"" + message + "\" buttons {\"OK\"} with icon stop'")
             .ExecuteAsync()
             .Task.Wait();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
          // TODO test
          Cli.Wrap("bash")
             .WithArguments(
                "xmessage - center \"" + message + "\"")
             .ExecuteAsync()
             .Task.Wait();
        }
      }
      catch
      {
        // quiet
      }
    }

  }
}

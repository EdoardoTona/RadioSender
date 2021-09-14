using CliWrap;
using Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            .ActivatePhotino();

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

using CliWrap;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Source.Microplus;
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
using System.Text.Json.Serialization;

namespace RadioSender;

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

      var builder = WebApplication.CreateBuilder(args);
      builder.Host.UseSerilog();
      builder.Host.UseHangfire();
      builder.Host.UseFilters();
      builder.Host.ActivatePhotino();
      builder.Services.AddHttpClient();

      // Sources
      builder.Host.FromRoc()
                  .FromSportidentCenter()
                  .FromSportidentSerial()
                  .FromTmFRadio()
                  .FromSirap()
                  .FromMicroplus()
      // Middleware
                  .ThroughDispatcher()
      // Targets
                  .ToUI()
                  .ToOribos()
                  .ToFile()
                  .ToSirap()
                  .ToTcp()
                  .ToHttp();

      builder.Services.AddHealthChecks();
      builder.Services.AddRazorPages();
      builder.Services.AddSignalR()
                      .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
      builder.Services.AddSingleton<HubEvents>();

      /////////////////////////////////////////////////////////////////////////////////


      var app = builder.Build();
      var env = app.Environment;

      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }

      app.UseStaticFiles(new StaticFileOptions
      {
        OnPrepareResponse = context =>
        {
          if (env.IsDevelopment())
            context.Context.Response.Headers.Append("Cache-Control", "no-cache");
          else
            context.Context.Response.Headers.Append("Cache-Control", "private, max-age=86400"); // 1 day
        }
      });

      app.UseHangfireDashboard();
      app.UseRouting();
      app.MapHealthChecks("healthz");
      app.MapRazorPages();
      app.MapHub<DeviceHub>("/deviceHub");
      app.MapHangfireDashboard();
      app.Run();

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

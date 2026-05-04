using CliWrap;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Source.Microplus;
using RadioSender.Hosts.Source.Mqtt;
using RadioSender.Hosts.Source.ROC;
using RadioSender.Hosts.Source.SIRAP;
using RadioSender.Hosts.Source.SportidentCenter;
using RadioSender.Hosts.Source.SportidentSerial;
using RadioSender.Hosts.Source.TmFRadio;
using RadioSender.Hosts.Target.File;
using RadioSender.Hosts.Target.Http;
using RadioSender.Hosts.Target.Oribos;
using RadioSender.Hosts.Target.PosPrinter;
using RadioSender.Hosts.Target.SIRAP;
using RadioSender.Hosts.Target.Tcp;
using RadioSender.Hosts.Target.UI;
using RadioSender.UI;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace RadioSender;

public static class Program
{
  public static int Main(string[] args)
  {
    try
    {
      var appsettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

      if (!File.Exists(appsettings))
      {
        using var stream = Assembly.GetExecutingAssembly()
                             .GetManifestResourceStream("RadioSender.appsettings.json")!;
        using var dest = File.Create(appsettings);
        stream.CopyTo(dest);
      }

      var configuration = new ConfigurationBuilder()
                              .SetBasePath(Directory.GetCurrentDirectory())
                              .AddJsonFile(appsettings, optional: true, reloadOnChange: true)
                              .Build();

      Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                    .WriteTo.EventLogSink()
                    .CreateLogger();

      var assembly = Assembly.GetExecutingAssembly().GetName();

      Log.Information("**** Starting up {application} {version} ****", assembly.Name, assembly.Version);

      var app = BuildApp(args);

      // On macOS, Photino must run on the main thread (AppKit requirement).
      // We start the web host on a background thread and run Photino here.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        var urls = configuration.GetSection("Urls").Get<string>() ?? "http://*:8082";
        var port = Regex.Match(urls, @"(?<=:)\d{2,5}").Value;

        // Start the web host on a background thread
        var cts = new CancellationTokenSource();
        var webHostThread = new Thread(() =>
        {
          try
          {
            app.Run();
          }
          catch (OperationCanceledException) { }
        });
        webHostThread.IsBackground = true;
        webHostThread.Start();

        // Give the web host a moment to start listening
        Thread.Sleep(1500);

        // Run Photino on the main thread (blocks until window closes)
        PhotinoHostedService.RunOnMainThread(port, () =>
        {
          cts.Cancel();
          app.StopAsync().Wait();
        });
      }
      else
      {
        app.Run();
      }

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

  private static WebApplication BuildApp(string[] args)
  {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
      Args = args,
      ContentRootPath = AppContext.BaseDirectory
    });
    builder.Host.UseSerilog();
    builder.Host.UseHangfire();
    builder.Host.UseFilters();
    builder.Host.ActivatePhotino();
    builder.Services.AddHttpClient();

    builder.Services.AddHostedService<HostOrchestrator>();

    // Sources
    builder.Host.FromRoc()
                .FromSportidentCenter()
                .FromSportidentSerial()
                .FromTmFRadio()
                .FromMqtt()
                .FromSirap()
                .FromMicroplus()
                .FromMicrogate()
    // Middleware
                .ThroughDispatcher()
    // Targets
                .ToUI()
                .ToOribos()
                .ToFile()
                .ToSirap()
                .ToTcp()
                .ToHttp();

    // Platform-specific targets
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      builder.Host.ToPrinter();
    }

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

    var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "RadioSender.wwwroot");
    IFileProvider staticFileProvider = Directory.Exists(wwwrootPath)
      ? new CompositeFileProvider(new PhysicalFileProvider(wwwrootPath), embeddedProvider)
      : embeddedProvider;

    app.UseStaticFiles(new StaticFileOptions
    {
      FileProvider = staticFileProvider,
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

    return app;
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

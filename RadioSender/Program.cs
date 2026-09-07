using CliWrap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using RadioSender.UI;
using RadioSender.Api;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

      using var app = BuildApp(args);

      // On macOS, Photino must run on the main thread (AppKit requirement).
      // Wait until the web host is listening before opening the window.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && app.Configuration.GetValue("Desktop:Enabled", true))
      {
        var urls = app.Configuration.GetSection("Urls").Get<string>() ?? "http://127.0.0.1:8082";
        var port = Regex.Match(urls, @"(?<=:)\d{2,5}").Value;

        app.StartAsync().GetAwaiter().GetResult();
        PhotinoHostedService.RunOnMainThread(port, app.Lifetime.StopApplication);
        app.WaitForShutdownAsync().GetAwaiter().GetResult();
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

    // The content root is the build output, so the default providers watch the
    // appsettings copies in bin/, which only change on rebuild. In Development
    // also watch the files in the project directory (the cwd under VS/dotnet run)
    // so editing them live-reloads the configuration. They are inserted right
    // after the default json providers to keep env vars and command line args
    // at higher precedence.
    if (builder.Environment.IsDevelopment())
    {
      var cwd = Directory.GetCurrentDirectory();
      if (!string.Equals(Path.TrimEndingDirectorySeparator(cwd), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
      {
        var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
        var insertAt = sources.Count;
        for (var i = 0; i < sources.Count; i++)
          if (sources[i] is JsonConfigurationSource)
            insertAt = i + 1;

        foreach (var file in new[] { "appsettings.json", $"appsettings.{builder.Environment.EnvironmentName}.json" })
        {
          var source = new JsonConfigurationSource { Path = Path.Combine(cwd, file), Optional = true, ReloadOnChange = true };
          source.ResolveFileProvider();
          sources.Insert(insertAt++, source);
        }
      }
    }

    builder.Host.UseSerilog();
    builder.Host.ActivatePhotino();
    builder.Services.AddHttpClient();

    builder.Services.AddFlowServices();
    builder.Services.AddHealthChecks();
    builder.Services.AddSignalR()
                    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    /////////////////////////////////////////////////////////////////////////////////

    var app = builder.Build();
    var env = app.Environment;

    if (env.IsDevelopment())
    {
      app.UseDeveloperExceptionPage();
    }

    app.MapFlowApi();

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
        if (env.IsDevelopment() || context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
          context.Context.Response.Headers.Append("Cache-Control", "no-cache");
        else
          context.Context.Response.Headers.Append("Cache-Control", "private, max-age=86400"); // 1 day
      }
    });

    app.UseRouting();
    app.MapHealthChecks("healthz");
    app.MapGet("/", () => Results.Redirect("/flows/index.html"));
    app.MapGet("/Flows", () => Results.Redirect("/flows/index.html"));

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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;

namespace RadioSender.Hosts.Target.File
{
  public record FileConfiguration
  {
    public string Path { get; set; }
    public FileFormat Format { get; set; } = FileFormat.Auto;
  }

  public enum FileFormat
  {
    Auto = 0,
    Csv,
    Json,
    Xml
  }
  public static class ConfigureFile
  {
    public static IHostBuilder ToFile(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:File:Enable", false))
          return;

        var files = context.Configuration.GetSection("Target:File:Files").Get<IEnumerable<FileConfiguration>>();

        foreach (var file in files)
        {
          services.AddSingleton<ITarget, FileTarget>(s => new FileTarget(file));
        }

      });

      return builder;
    }
  }
}

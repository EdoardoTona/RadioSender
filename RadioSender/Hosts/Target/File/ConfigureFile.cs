using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Target.File
{
  public record FileConfiguration : FilterableConfiguration
  {
    public string Path { get; init; }
    public string Format { get; init; }
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
          services.AddSingleton<ITarget, FileTarget>(s => new FileTarget(s.GetServices<IFilter>(), file));
        }

      });

      return builder;
    }
  }
}

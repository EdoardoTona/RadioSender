using Hangfire;
using Hangfire.Console;
using Hangfire.Console.Extensions;
using Hangfire.MemoryStorage;
using Hangfire.MissionControl;
using Microsoft.Extensions.Hosting;
using System;
using System.Reflection;

namespace RadioSender.Hosts.Common
{
  public static class Hangfire
  {
    public static IHostBuilder UseHangfire(this IHostBuilder builder)
    {

      builder.ConfigureServices((context, services) =>
      {
        services.AddHangfire(setup =>
        {
          setup
           .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
           //.UseSimpleAssemblyNameTypeSerializer()
           //.UseRecommendedSerializerSettings()
           .UseMemoryStorage(new MemoryStorageOptions() { CountersAggregateInterval = TimeSpan.FromMilliseconds(500) })
           .UseConsole()
           .UseMissionControl(Assembly.GetExecutingAssembly());
        });

        services.AddHangfireServer(setup =>
        {
          setup.CancellationCheckInterval = TimeSpan.FromMinutes(2);
          setup.Queues = new string[] { "default" };
          setup.ServerName = "Radiosender";
          setup.WorkerCount = Environment.ProcessorCount * 5;
          setup.ServerCheckInterval = TimeSpan.FromSeconds(0.1);
        });

        services.AddHangfireConsoleExtensions();
      });

      return builder;
    }
  }
}

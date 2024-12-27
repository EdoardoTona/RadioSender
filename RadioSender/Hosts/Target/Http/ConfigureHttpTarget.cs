using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Target.Http;

public record HttpTargetConfiguration : FilterableConfiguration
{
  public string? Url { get; set; }
  public string? Method { get; set; }
  public bool EnsureSuccessStatusCode { get; set; }
}

public static class ConfigureHttpTarget
{
  public static IHostBuilder ToHttp(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Target:HTTP:Enable", false))
        return;

      var clients = context.Configuration.GetSection("Target:HTTP:Targets").Get<IEnumerable<HttpTargetConfiguration>>();

      foreach (var client in clients)
      {
        services.AddSingleton<ITarget>(sp =>
          new HttpTarget(
            sp.GetServices<IFilter>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IBackgroundJobClient>(),
            client
            )
        );
      }

    });

    return builder;
  }
}


using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Target.OResults
{
  public record OResultsConfiguration : FilterableConfiguration
  {
    public string Host { get; init; } = "https://api.oresults.eu/";
    public string Path { get; init; } = "/punches/external";
    public string? ApiKey { get; init; }
    public bool UseUtc { get; init; } = true;
    public bool IgnoreCompetitorIdType { get; init; } = false;
  }

  public static class ConfigureOResults
  {
    public static IHostBuilder ToOResults(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Target:OResults:Enable", false))
          return;

        var servers = context.Configuration.GetSection("Target:OResults:Servers").Get<IEnumerable<OResultsConfiguration>>();

        if (servers == null)
          return;

        services.AddHttpClient();

        foreach (var server in servers)
        {
          services.AddSingleton<ITarget>(sp => new OResultsService(
            sp.GetRequiredService<FilterService>(),
            sp.GetRequiredService<IBackgroundJobClient>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            server));
        }
      });

      return builder;
    }
  }
}

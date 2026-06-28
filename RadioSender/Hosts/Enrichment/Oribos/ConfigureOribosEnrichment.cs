using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Net.Http;

namespace RadioSender.Hosts.Enrichment.Oribos
{
  public record OribosEnrichmentConfiguration : FilterableConfiguration
  {
    public bool Enable { get; init; } = true;        // enables this instance
    public string Name { get; init; } = "Oribos";    // key used in filters' Enrichers list
    public string? Host { get; init; }               // e.g. http://localhost:8080
    public bool Merged { get; init; } = false;       // fullweb querystring merged=
    public bool EmitStatusChanges { get; init; } = false; // generate status change events
  }

  public static class ConfigureOribosEnrichment
  {
    public static IHostBuilder WithOribosEnrichment(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        if (!context.Configuration.GetValue("Enrichment:Oribos:Enable", false))
          return;

        var sources = context.Configuration.GetSection("Enrichment:Oribos:Sources").Get<IEnumerable<OribosEnrichmentConfiguration>>();
        if (sources == null)
          return;

        services.AddHttpClient();

        foreach (var source in sources)
        {
          if (!source.Enable)
            continue;

          var config = source; // capture per iteration

          // one instance per configured source, lazily built and shared across both
          // contracts (a single longpolling powering both enrichment and status events).
          // DispatcherService is resolved lazily to break the DI cycle
          // FilterService -> IEnrichmentSource (this) -> DispatcherService -> FilterService.
          OribosService? instance = null;
          OribosService Factory(System.IServiceProvider sp) => instance ??= new OribosService(
            new System.Lazy<DispatcherService>(sp.GetRequiredService<DispatcherService>),
            sp.GetRequiredService<IHttpClientFactory>(),
            config);

          services.AddSingleton<IEnrichmentSource>(Factory);
          services.AddSingleton<IRadioSenderHost>(Factory);
        }
      });

      return builder;
    }
  }
}

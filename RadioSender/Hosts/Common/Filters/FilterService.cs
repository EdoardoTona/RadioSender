using Microsoft.Extensions.Options;
using RadioSender.Hosts.Enrichment;
using Serilog;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Hosts.Common.Filters;

public sealed class FilterService : System.IDisposable
{
  readonly IOptionsMonitor<FiltersConfiguration> _optionsMonitorFilters;
  readonly List<System.IDisposable> _subscriptions = [];
  readonly IReadOnlyDictionary<string, IEnrichmentSource> _enrichers;

  public FilterService(
    IOptionsMonitor<FiltersConfiguration> optionsMonitorFilters,
    IEnumerable<IEnrichmentSource> enrichers)
  {
    _optionsMonitorFilters = optionsMonitorFilters;
    _subscriptions.Add(_optionsMonitorFilters.OnChange((filtersConfig, name) => UpdateFilter())!);

    // index enrichers by name; last one wins if duplicated names
    var map = new Dictionary<string, IEnrichmentSource>();
    foreach (var e in enrichers)
      map[e.Name] = e;
    _enrichers = map;
  }

  public void UpdateFilter()
  {
    Log.Information("Filters updated");
  }

  public Punch? Transform(string? filterName, Punch? punch)
  {
    var filter = _optionsMonitorFilters.CurrentValue.List.GetFilter(filterName);
    var transformed = filter.Transform(punch);
    return Enrich(filter, transformed);
  }

  public IEnumerable<Punch> Transform(string? filterName, IEnumerable<Punch>? punches)
  {
    var filter = _optionsMonitorFilters.CurrentValue.List.GetFilter(filterName);
    var transformed = filter.Transform(punches);

    if (_enrichers.Count == 0 || filter.Enrichers.Count == 0)
      return transformed;

    return transformed.Select(p => Enrich(filter, p)!);
  }

  // Applies the filter's enrichers in the configured order; a later enricher can overwrite
  // fields populated by an earlier one. No-op when there are no enrichers.
  private Punch? Enrich(IFilter filter, Punch? punch)
  {
    if (punch == null || _enrichers.Count == 0 || filter.Enrichers.Count == 0)
      return punch;

    foreach (var name in filter.Enrichers)
    {
      if (!_enrichers.TryGetValue(name, out var enricher))
        continue;

      var enriched = enricher.Enrich(punch);

      if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) && !ReferenceEquals(enriched.Competitor, punch.Competitor))
        Log.Debug("Enricher {enricher} on {source}/{competitorId}: bib={bib} name={name}",
          name, enriched.SourceId, enriched.CompetitorId, enriched.Competitor?.Bib, enriched.Competitor?.Name);

      punch = enriched;
    }

    return punch;
  }

  public void Dispose()
  {
    foreach (var subscription in _subscriptions)
      subscription.Dispose();
  }
}

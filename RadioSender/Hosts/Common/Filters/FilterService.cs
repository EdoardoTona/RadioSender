using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common.Filters;

public sealed class FilterService : IDisposable
{
  readonly IOptionsMonitor<FiltersConfiguration> _optionsMonitorFilters;
  readonly List<IDisposable> _subscriptions = [];

  public FilterService(IOptionsMonitor<FiltersConfiguration> optionsMonitorFilters)
  {
    _optionsMonitorFilters = optionsMonitorFilters;
    _subscriptions.Add(_optionsMonitorFilters.OnChange((filtersConfig, name) => UpdateFilter())!);
  }

  public void UpdateFilter()
  {
    Log.Information("Filters updated");
  }

  public Punch? Transform(string? filterName, Punch? punch)
  {
    var filter = _optionsMonitorFilters.CurrentValue.List.GetFilter(filterName);
    return filter.Transform(punch);
  }

  public IEnumerable<Punch> Transform(string? filterName, IEnumerable<Punch>? punches)
  {
    var filter = _optionsMonitorFilters.CurrentValue.List.GetFilter(filterName);
    return filter.Transform(punches);
  }
  public void Dispose()
  {
    foreach (var subscription in _subscriptions)
      subscription.Dispose();
  }
}

using Microsoft.Extensions.Options;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common;

public sealed class DispatcherService : IDisposable
{
  private readonly DispatcherConfiguration _configuration;
  private readonly IOptionsMonitor<FiltersConfiguration> _optionsMonitorFilters;

  private IFilter _filter;
  private readonly IEnumerable<ITarget> _targets;

  private readonly HashSet<Punch> _punches = [];

  public event EventHandler? RequestPing;
  private readonly List<IDisposable> _subscriptions = [];

  public DispatcherService(
    IOptionsMonitor<FiltersConfiguration> optionsMonitorFilters,
    IEnumerable<ITarget> targets,
    DispatcherConfiguration configuration
    )
  {
    _targets = targets;
    _configuration = configuration;

    _optionsMonitorFilters = optionsMonitorFilters;

    _subscriptions.Add(_optionsMonitorFilters.OnChange((filtersConfig, name) => UpdateFilter())!);
    UpdateFilter();
  }

  public void UpdateFilter()
  {
    Log.Information("Updated filters");
    var filters = _optionsMonitorFilters.CurrentValue.List;
    _filter = filters.GetFilter(_configuration.Filter);
  }

  public void ResendPunches()
  {
    _ = Task.WhenAll(_targets.Select(t => t.SendDispatch(new PunchDispatch([.. _punches], null), default)));
  }

  public void Ping()
  {
    RequestPing?.Invoke(this, EventArgs.Empty);
  }

  public void PushDispatch(PunchDispatch dispatch)
  {
    if (dispatch.Punches != null)
    {
      var punches = _filter.Transform(dispatch.Punches);

      var toBeForwardedPunch = new List<Punch>();
      foreach (var punch in punches)
      {
        if (_punches.Contains(punch))
        {
          Log.Verbose("Detected duplicated punch " + punch);
          continue;
        }

        _punches.Add(punch);
        toBeForwardedPunch.Add(punch);
      }

      dispatch = dispatch with { Punches = toBeForwardedPunch };
    }

    _ = Task.WhenAll(_targets.Select(t => t.SendDispatch(dispatch, default)));
  }

  public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
  {
    var toBeForwardedDispatcher = new List<PunchDispatch>();
    foreach (var dispatch in dispatches)
    {
      if (dispatch.Punches == null)
        continue;

      var punches = _filter.Transform(dispatch.Punches);

      var toBeForwardedPunch = new List<Punch>();
      foreach (var punch in punches)
      {
        if (_punches.Contains(punch))
        {
          Log.Information("Detected duplicated punch " + punch);
          continue;
        }

        _punches.Add(punch);
        toBeForwardedPunch.Add(punch);
      }

      toBeForwardedDispatcher.Add(dispatch with { Punches = toBeForwardedPunch });
    }

    _ = Task.WhenAll(_targets.Select(t => t.SendDispatches(toBeForwardedDispatcher, default)));
  }

  public void Dispose()
  {
    foreach (var item in _subscriptions)
      item.Dispose();
  }
}

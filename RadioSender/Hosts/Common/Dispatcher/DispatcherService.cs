using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common;

public sealed class DispatcherService(
  FilterService filterService,
  IEnumerable<ITarget> targets,
  DispatcherConfiguration configuration
    ) : IDisposable
{
  private readonly ConcurrentDictionary<string, Punch> _punches = [];

  public event EventHandler? RequestPing;
  private readonly List<IDisposable> _subscriptions = [];

  public void ResendPunches()
  {
    _ = Task.WhenAll(targets.Select(t => t.SendDispatch(new PunchDispatch([.. _punches.Values], null), default)));
  }

  public void Ping()
  {
    RequestPing?.Invoke(this, EventArgs.Empty);
  }

  public void PushDispatch(PunchDispatch dispatch)
  {
    if (dispatch.Punches != null)
    {
      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);

      var toBeForwardedPunch = new List<Punch>();
      foreach (var punch in punches)
      {
        var key = punch.ToString();
        if (_punches.ContainsKey(key))
        {
          Log.Verbose("Detected duplicated punch " + punch);
          continue;
        }

        _punches.TryAdd(key, punch);
        toBeForwardedPunch.Add(punch);
      }

      dispatch = dispatch with { Punches = toBeForwardedPunch };
    }

    _ = Task.WhenAll(targets.Select(t => t.SendDispatch(dispatch, default)));
  }

  public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
  {
    var toBeForwardedDispatcher = new List<PunchDispatch>();
    foreach (var dispatch in dispatches)
    {
      if (dispatch.Punches == null)
        continue;

      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);

      var toBeForwardedPunch = new List<Punch>();
      foreach (var punch in punches)
      {
        var key = punch.ToString();
        if (_punches.ContainsKey(key))
        {
          Log.Information("Detected duplicated punch " + punch);
          continue;
        }

        _punches.TryAdd(key, punch);
        toBeForwardedPunch.Add(punch);
      }

      toBeForwardedDispatcher.Add(dispatch with { Punches = toBeForwardedPunch });
    }

    _ = Task.WhenAll(targets.Select(t => t.SendDispatches(toBeForwardedDispatcher, default)));
  }

  public void Dispose()
  {
    foreach (var item in _subscriptions)
      item.Dispose();
  }
}

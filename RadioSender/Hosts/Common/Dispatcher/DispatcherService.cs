using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common
{
  public class DispatcherService
  {
    private readonly DispatcherConfiguration _configuration;
    private readonly IFilter _filter;
    private readonly IEnumerable<ITarget> _targets;

    private readonly HashSet<Punch> _punches = new();

    public event EventHandler RequestPing;

    public DispatcherService(
      IEnumerable<IFilter> filters,
      IEnumerable<ITarget> targets,
      DispatcherConfiguration configuration
      )
    {
      _targets = targets;
      _configuration = configuration;
      _filter = filters.GetFilter(_configuration.Filter);

    }

    public void ResendPunches()
    {
      _ = Task.WhenAll(_targets.Select(t => t.SendDispatch(new PunchDispatch(_punches.ToArray(), null), default)));
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
            Log.Information("Detected duplicated punch " + punch);
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

  }
}

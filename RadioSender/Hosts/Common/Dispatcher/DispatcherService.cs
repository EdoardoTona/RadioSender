using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common
{
  public class DispatcherService
  {
    private readonly DispatcherConfiguration _configuration;
    private readonly IFilter _filter = Filter.Invariant;
    private readonly IEnumerable<ITarget> _targets;

    private readonly HashSet<Punch> _punches = new();

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

    //public void PushPunch(Punch? punch)
    //{
    //  punch = _filter.Transform(punch);

    //  if (punch == null)
    //    return;

    //  if (_punches.Contains(punch))
    //  {
    //    Log.Information("Detected duplicated punch " + punch);
    //    return;
    //  }

    //  Log.Information("Received punch " + punch);
    //  _punches.Add(punch);

    //  if (punch != null)
    //    _ = Task.WhenAll(_targets.Select(t => t.SendPunch(punch, default)));
    //}

    //public void PushPunches(IEnumerable<Punch> punches)
    //{
    //  punches = _filter.Transform(punches);

    //  if (!punches.Any())
    //    return;

    //  var toBeForwarded = new List<Punch>();

    //  foreach (var punch in punches)
    //  {
    //    if (_punches.Contains(punch))
    //      Log.Information("Detected duplicated punch " + punch);
    //    else
    //    {
    //      Log.Information("Received punch " + punch);
    //      _punches.Add(punch);
    //      toBeForwarded.Add(punch);
    //    }
    //  }

    //  if (toBeForwarded.Any())
    //    _ = Task.WhenAll(_targets.Select(t => t.SendPunches(toBeForwarded, default)));
    //}


    public void PushDispatch(PunchDispatch dispatch)
    {
      var punches = _filter.Transform(dispatch.Punches);

      if (!punches.Any())
        return;

      var toBeForwardedPunch = new List<Punch>();
      foreach (var punch in punches)
      {
        if (_punches.Contains(punch))
        {
          Log.Information("Detected duplicated punch " + punch);
          continue;
        }

        Log.Information("Received punch " + punch);
        _punches.Add(punch);
        toBeForwardedPunch.Add(punch);
      }

      if (!toBeForwardedPunch.Any())
        return;

      dispatch = dispatch with { Punches = toBeForwardedPunch };
      _ = Task.WhenAll(_targets.Select(t => t.SendDispatch(dispatch, default)));
    }

    public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
    {
      var toBeForwardedDispatcher = new List<PunchDispatch>();
      foreach (var dispatch in dispatches)
      {
        var punches = _filter.Transform(dispatch.Punches);
        if (!punches.Any())
          return;

        var toBeForwardedPunch = new List<Punch>();
        foreach (var punch in punches)
        {
          if (_punches.Contains(punch))
          {
            Log.Information("Detected duplicated punch " + punch);
            continue;
          }

          Log.Information("Received punch " + punch);
          _punches.Add(punch);
          toBeForwardedPunch.Add(punch);
        }

        if (toBeForwardedPunch.Any())
          toBeForwardedDispatcher.Add(dispatch with { Punches = toBeForwardedPunch });
      }

      if (toBeForwardedDispatcher.Any())
        _ = Task.WhenAll(_targets.Select(t => t.SendDispatches(toBeForwardedDispatcher, default)));
    }

  }
}

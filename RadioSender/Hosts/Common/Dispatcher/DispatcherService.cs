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
      _ = Task.WhenAll(_targets.Select(t => t.SendPunches(_punches, default)));
    }

    public void PushPunch(Punch punch)
    {
      punch = _filter.Transform(punch);

      if (punch == null)
        return;

      if (_punches.Contains(punch))
      {
        Log.Information("Detected duplicated punch " + punch);
        return;
      }

      Log.Information("Received punch " + punch);
      _punches.Add(punch);

      if (punch != null)
        _ = Task.WhenAll(_targets.Select(t => t.SendPunch(punch, default)));
    }

    public void PushPunches(IEnumerable<Punch> punches)
    {
      punches = _filter.Transform(punches);

      if (punches == null || !punches.Any())
        return;

      var notDuplicated = new List<Punch>();

      foreach (var punch in punches)
      {
        if (_punches.Contains(punch))
          Log.Information("Detected duplicated punch " + punch);
        else
        {
          Log.Information("Received punch " + punch);
          _punches.Add(punch);
          notDuplicated.Add(punch);
        }
      }

      if (notDuplicated.Any())
        _ = Task.WhenAll(_targets.Select(t => t.SendPunches(notDuplicated, default)));
    }

  }
}

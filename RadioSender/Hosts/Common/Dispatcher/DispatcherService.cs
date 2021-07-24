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

    public void PushPunch(Punch punch)
    {
      punch = _filter.Transform(punch);

      if (punch == null)
        return;

      Log.Information("Received punch " + punch);
      // TODO handle duplicate punches

      if (punch != null)
        _ = Task.WhenAll(_targets.Select(t => t.SendPunch(punch, default)));
    }

    public void PushPunches(IEnumerable<Punch> punches)
    {
      punches = _filter.Transform(punches);

      if (punches == null || !punches.Any())
        return;

      foreach (var punch in punches)
        Log.Information("Received punch " + punch);
      // TODO handle duplicate punches

      _ = Task.WhenAll(_targets.Select(t => t.SendPunches(punches, default)));
    }

  }
}

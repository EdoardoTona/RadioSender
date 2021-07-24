using Hangfire;
using RadioSender.Hosts.Target;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common
{
  public class DispatcherService
  {
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IEnumerable<ITarget> _targets;
    public DispatcherService(
      IBackgroundJobClient backgroundJobClient,
      IEnumerable<ITarget> targets)
    {
      _targets = targets;
    }

    public void PushPunch(Punch punch)
    {
      if (punch == null) return;

      Log.Information("Received punch " + punch);
      // TODO handle duplicate punches

      _ = Task.WhenAll(_targets.Select(t => t.SendPunch(punch, default)));
    }

    public void PushPunches(IEnumerable<Punch> punches)
    {
      if (punches == null) return;
      // TODO handle duplicate punches

      _ = Task.WhenAll(_targets.Select(t => t.SendPunches(punches, default)));
    }

  }
}

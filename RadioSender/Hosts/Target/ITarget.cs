using RadioSender.Hosts.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target
{
  public interface ITarget
  {
    Task SendPunch(Punch punch, CancellationToken ct = default);
    Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default);
  }
}

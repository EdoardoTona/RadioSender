using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target
{
  public interface ITarget
  {
    void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration);
    Task SendPunch(Punch punch, CancellationToken ct = default);
    Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default);
  }
}

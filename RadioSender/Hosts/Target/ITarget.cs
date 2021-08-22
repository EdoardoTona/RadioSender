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
    Task SendPunch(PunchDispatch dispatch, CancellationToken ct = default);
    Task SendPunches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default);
  }
}

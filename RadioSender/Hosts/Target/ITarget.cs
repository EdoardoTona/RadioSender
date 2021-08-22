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
    Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default);
    Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default);
  }
}

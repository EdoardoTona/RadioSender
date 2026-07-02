using RadioSender.Hosts.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target
{
  public record TargetDescriptor(string Id, string Name, bool ManualReplay = true);

  public interface ITarget
  {
    TargetDescriptor Descriptor => new(GetType().Name, GetType().Name);
    Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default);
    Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default);
  }
}

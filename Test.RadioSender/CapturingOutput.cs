using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Test.RadioSender;

internal sealed class CapturingOutput : IFlowOutput, IDispatchSink
{
  public ConcurrentQueue<Punch> Received { get; } = new();
  public ValueTask PublishAsync(Punch punch, CancellationToken cancellationToken)
  { cancellationToken.ThrowIfCancellationRequested(); Received.Enqueue(punch); return ValueTask.CompletedTask; }
  public void PushDispatch(PunchDispatch dispatch)
  { foreach (var punch in dispatch.Punches ?? []) Received.Enqueue(punch); }
  public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
  { foreach (var dispatch in dispatches) PushDispatch(dispatch); }
}

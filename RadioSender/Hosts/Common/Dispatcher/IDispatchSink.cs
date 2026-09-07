using Serilog;
using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common;

// Transport decoders publish data without knowing how it is routed or deduplicated.
public interface IDispatchSink
{
  ILogger Logger => Log.Logger;
  void SetSourceState(string status, string? detail = null) { }
  void PushDispatch(PunchDispatch dispatch);
  void PushDispatches(IEnumerable<PunchDispatch> dispatches);
}

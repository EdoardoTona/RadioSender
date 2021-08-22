using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common
{
  public record Punch(string Card, DateTime Time, int Control, PunchControlType ControlType = PunchControlType.Unknown);
  public abstract record GraphElement(string? Name, int? LatencyMs, int? SignalStength);
  public record Node(Guid Id, string? Name, int? LatencyMs, int? SignalStength) : GraphElement(Name, LatencyMs, SignalStength);
  public record Hop(Guid From, Guid To, int? LatencyMs, int? SignalStength) : GraphElement(null, LatencyMs, SignalStength);
  public record PunchDispatch(Punch Punch, IEnumerable<Hop>? Hops);

  public enum PunchControlType
  {
    Unknown = 0,
    Control,
    Finish,
    Clear,
    Check,
    Start
  }
}

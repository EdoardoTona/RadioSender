using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common
{
  public abstract record GraphElement(string? Name, long? LatencyMs, int? SignalStength);
  public record NodeNew(string Id, string? Name, long? LatencyMs, int? SignalStength) : GraphElement(Name, LatencyMs, SignalStength)
  {
    public static readonly NodeNew Localhost = new(Guid.Empty.ToString(), "localhost", 0, 1);
  }
  public record Hop(string From, string To, long? LatencyMs, int? SignalStength) : GraphElement(null, LatencyMs, SignalStength);
  public record PunchDispatch(IEnumerable<Punch>? Punches = null, IEnumerable<Hop>? Hops = null, IEnumerable<NodeNew>? Nodes = null);
  public record Punch(string Card, DateTime Time, int Control, PunchControlType ControlType = PunchControlType.Unknown);
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

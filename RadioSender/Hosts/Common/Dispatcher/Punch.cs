using System;

namespace RadioSender.Hosts.Common
{
  public record Punch
  {
    public string Card { get; init; }
    public DateTime Time { get; init; }
    public int Control { get; init; }
    public PunchControlType ControlType { get; init; } = PunchControlType.Unknown;
  }

  public enum PunchControlType
  {
    Control,
    Finish,
    Clear,
    Check,
    Start,
    Unknown
  }
}

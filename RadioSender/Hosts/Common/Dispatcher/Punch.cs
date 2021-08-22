using System;

namespace RadioSender.Hosts.Common
{
  public record Punch
  {
    // TODO sender information
    public string Card { get; init; } = null!;
    public DateTime Time { get; init; }
    public int Control { get; init; }
    public PunchControlType ControlType { get; init; } = PunchControlType.Unknown;
  }

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

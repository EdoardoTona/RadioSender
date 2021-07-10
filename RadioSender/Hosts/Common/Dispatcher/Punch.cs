using System;

namespace RadioSender.Hosts.Common
{
  public record Punch(string Card, DateTime Time, int Control, PunchControlType ControlType);
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

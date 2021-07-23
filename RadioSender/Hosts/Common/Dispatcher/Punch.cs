using System;

namespace RadioSender.Hosts.Common
{
  public record Punch(string Card, DateTime Time, int Control, PunchControlType OriginalControlType)
  {
    public PunchControlType ControlType
    {
      get
      {
        if (OriginalControlType != PunchControlType.Unknown)
          return OriginalControlType;

        if (Control == 999 || (Control >= 25 && Control <= 30) || (Control >= 2 && Control <= 10))
        {
          // 999 for OE2010
          // 10 for OLA
          // 2-9 are unknown... fallback on finish
          return PunchControlType.Finish;
        }
        else if (Control >= 21 && Control <= 24)
        {
          return PunchControlType.Start;
        }
        else if (Control == 1 || (Control >= 16 && Control <= 20))
        {
          // 1 is suggested by Sportident to avoid flashing on card (model 11, SIAC)
          return PunchControlType.Clear;
        }
        else if (Control >= 11 && Control <= 15)
        {
          return PunchControlType.Check;
        }
        else
          return PunchControlType.Control;

      }
    }
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

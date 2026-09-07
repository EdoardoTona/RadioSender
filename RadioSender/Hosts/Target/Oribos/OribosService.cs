using RadioSender.Hosts.Common;
using System;

namespace RadioSender.Hosts.Target.Oribos;

public static class OribosService
{
  public static string? BuildPunchPath(Punch punch)
  {
    if ((punch.CompetitorIdType == CompetitorIdType.PunchingCard || punch.NetTime) &&
        (punch.Cancellation || punch.CompetitorStatus != CompetitorStatus.Unknown)) return null;
    string? url;
    if (punch.CompetitorIdType == CompetitorIdType.PunchingCard)
    {
      if (punch.NetTime) return null;

      url = punch.ControlType switch
      {
        PunchControlType.Finish => $"/finish.html?card={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}",
        PunchControlType.Start => $"/start.html?card={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Clear => $"/clear.html?card={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        PunchControlType.Check => $"/check.html?card={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
        _ => $"/radiotime.html?card={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
      };
    }
    else if (punch.CompetitorIdType == CompetitorIdType.BibNumber)
    {

      if (punch.NetTime)
      {
        url = punch.ControlType switch
        {
          PunchControlType.Finish => $"/algetime.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}",
          PunchControlType.Control => $"/algetime.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
          PunchControlType.Unknown => $"/algetime.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}",
          _ => null
        };

        if (url == null) return null;

      }
      else
      {
        if (punch.CompetitorStatus != CompetitorStatus.Unknown)
        {
          url = punch.CompetitorStatus switch
          {
            CompetitorStatus.DNS => $"/changestate.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&state=np",
            CompetitorStatus.Running => $"/changestate.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&state=ga",
            CompetitorStatus.WaitingStart => $"/changestate.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&state=ip",
            _ => null
          };
        }
        else if (punch.Cancellation)
        {
          url = punch.ControlType switch
          {
            PunchControlType.Finish => $"/cronofinish.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time=00.00.00&type=1&abs=0",
            PunchControlType.Start => $"/cronostart.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time=00.00.00&type=1&abs=0",
            _ => null
          };
        }
        else
        {
          url = punch.ControlType switch
          {
            PunchControlType.Finish => $"/cronofinish.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&abs=1",
            PunchControlType.Start => $"/cronostart.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&abs=1",
            PunchControlType.Control => $"/cronoradio.html?pett={Uri.EscapeDataString(punch.CompetitorId)}&time={punch.Time:HH:mm:ss.fff}&point={punch.Control}&abs=1",
            _ => null
          };
        }

      }

    }
    else
    {
      return null;
    }

    return url;
  }

}

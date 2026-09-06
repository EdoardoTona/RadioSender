using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using System;

namespace RadioSender.Flow.Modules;

internal static class FormattedOutput
{
  public static byte[]? Encode(Punch punch, string format, bool tcp, out string? reason)
  {
    reason = null;
    if (punch.Cancellation && !FormatStringHelper.UsesPlaceholder(format, "Cancellation"))
      reason = "The format cannot represent cancellations. Add {Cancellation}.";
    var hasStatus = FormatStringHelper.UsesPlaceholder(format, "Status");
    if (punch.CompetitorStatus != CompetitorStatus.Unknown && !hasStatus)
    {
      if (!tcp || !FormatStringHelper.UsesPlaceholder(format, "Time"))
        reason = "The format cannot represent status changes. Add {Status}.";
      else
      {
        var seconds = punch.CompetitorStatus switch
        {
          CompetitorStatus.Running or CompetitorStatus.WaitingStart => 0,
          CompetitorStatus.DNS => 1, CompetitorStatus.DNF => 2, CompetitorStatus.MP => 3,
          CompetitorStatus.DSQ => 4, CompetitorStatus.OverTime => 5, _ => -1
        };
        if (seconds >= 0) punch = punch with { Time = DateTime.MinValue.AddSeconds(seconds) };
      }
    }
    return reason == null ? FormatStringHelper.GetBytes(punch, format) : null;
  }
}

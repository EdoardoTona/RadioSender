using Microsoft.IO;
using RadioSender.Hosts.Common;
using System;
using System.IO;
using System.Text;

namespace RadioSender.Hosts.Target.SIRAP;

public static class SirapClient
{
  private static readonly RecyclableMemoryStreamManager _memoryManager = new();
    public static byte[]? Encode(Punch punch, int version, TimeSpan zeroTime) => GetBytes(punch, version, zeroTime);

    private static byte[]? GetBytes(Punch punch, int version, TimeSpan zeroTime)
    {
      // SIRAP does not support cancellations
      if (punch.Cancellation)
        return null;

      // SIRAP is a chip (SI card) protocol. Prefer the card resolved by enrichment;
      // fall back to CompetitorId only when no specific card is available
      // (valid only if the punch itself is a punching card).
      var cardStr = punch.Competitor?.Card
        ?? (punch.CompetitorIdType is CompetitorIdType.PunchingCard or CompetitorIdType.Unknown ? punch.CompetitorId : null);

      if (!int.TryParse(cardStr, out int chipNo))
        return null; // a numeric card is required by SIRAP

      using var ms = _memoryManager.GetStream();
      using var bw = new BinaryWriter(ms);

      if (version == 2)
      {
        string name = "Radiosender";
        bw.Write((byte)name.Length);
        Span<byte> nameBuffer = new byte[20];
        Encoding.UTF8.GetBytes(name, nameBuffer);
        bw.Write(nameBuffer);
      }

      bw.Write((byte)0); // 0=punch, 255=Triggered time
      bw.Write((ushort)punch.Control);

      bw.Write(chipNo);
      bw.Write((int)punch.Time.DayOfWeek); // Day information from SI punch, sunday = 0

      int time;
      if (punch.Time == default)
      {
        time = 36000001; // invalid time
      }
      else if (version == 2)
      {
        // 1/100 resolution
        time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 10 - (int)zeroTime.TotalMilliseconds / 10;
        if (time < 0)
          time += 100 * 3600 * 24;
      }
      else
      {
        // 1/10 resolution
        time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 100 - (int)zeroTime.TotalMilliseconds / 100;
        if (time < 0)
          time += 10 * 3600 * 24;
      }

      bw.Write(time);

      return ms.ToArray();
    }

}

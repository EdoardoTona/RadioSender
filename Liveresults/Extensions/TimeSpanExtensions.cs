using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Liveresults
{
  public static class TimeSpanExtensions
  {
    public static string ToHMS(this TimeSpan timeSpan)
    {
      if (timeSpan.TotalMinutes < 60)
        return $"{Math.Floor(timeSpan.TotalMinutes)}:{timeSpan.Seconds.ToString("00")}";
      else
        return $"{Math.Floor(timeSpan.TotalHours)}:{timeSpan.Minutes.ToString("00")}:{timeSpan.Seconds.ToString("00")}";
    }
  }
}

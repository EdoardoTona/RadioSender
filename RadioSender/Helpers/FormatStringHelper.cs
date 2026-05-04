using RadioSender.Hosts.Common;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioSender.Helpers
{
  public static partial class FormatStringHelper
  {

    static object MapKey(Punch punch, string key)
    {
      return key switch
      {
        "Card" or "card" or "Bib" or "bib" or "CompetitorId" or "competitorid" => punch.CompetitorId,
        "CompetitorIdType" or "competitoridtype" => punch.CompetitorIdType,
        "Control" or "control" => punch.Control,
        "ControlType" or "controltype" => punch.ControlType,
        "Type" or "type" => punch.ControlTypeShort ?? "",
        "Time" or "time" => punch.Time,
        "Source" or "source" => punch.SourceId,
        "Cancellation" or "cancellation" => punch.Cancellation ? "ANN" : "",
        "Status" or "status" => punch.CompetitorStatus == CompetitorStatus.Unknown ? "" : punch.CompetitorStatus.ToString(),
        "NetTime" or "nettime" => punch.NetTime,
        "UnixS" or "unixs" => new DateTimeOffset(punch.Time).ToUnixTimeSeconds(),
        "UnixMs" or "unims" => new DateTimeOffset(punch.Time).ToUnixTimeMilliseconds(),
        "CRLF" => "\r\n",
        "CR" => '\r',
        "LF" => '\n',
        _ => "",
      };
    }

    public static string GetString(Punch punch, string format)
    {
      return FormatterRegex().Replace(format, (match) =>
      {
        var capture = match.Groups?.OfType<Group>().Skip(1).Select((group) => group.Value).ToArray();
        if (!(capture?.FirstOrDefault() is string key && MapKey(punch, key) is object value))
          return match.Value;

        return capture.Length > 1 && value is IFormattable formattable ? formattable.ToString(capture[1], CultureInfo.InvariantCulture) : value.ToString() ?? "";
      });
    }

    public static byte[] GetBytes(Punch punch, string format)
    {
      return Encoding.UTF8.GetBytes(GetString(punch, format));
    }

    [GeneratedRegex(@"\{([^\{\}]+?)(?:\:([^\{\}]*))?\}", RegexOptions.Multiline)]
    private static partial Regex FormatterRegex();
  }
}

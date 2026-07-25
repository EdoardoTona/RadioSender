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
        "CompetitorId" or "competitorid" => punch.CompetitorId, // the id whatever its type
        // prefer the enriched value; fall back to CompetitorId when its type matches
        "Bib" or "bib" => punch.Competitor?.Bib ?? (punch.CompetitorIdType == CompetitorIdType.BibNumber ? punch.CompetitorId : ""),
        "Card" or "card" => punch.Competitor?.Card ?? (punch.CompetitorIdType == CompetitorIdType.PunchingCard ? punch.CompetitorId : ""),
        "Card2" or "card2" => punch.Competitor?.Card2 ?? "",
        "Name" or "name" => punch.Competitor?.Name ?? "",
        "Class" or "class" => punch.Competitor?.Class ?? "",
        "Nation" or "nation" => punch.Competitor?.Nation ?? "",
        "ClubId" or "clubid" => punch.Competitor?.ClubId ?? "",
        "ClubName" or "clubname" or "Club" or "club" => punch.Competitor?.ClubName ?? "",
        "ClubNation" or "clubnation" => punch.Competitor?.ClubNation ?? "",
        "StartTime" or "starttime" => punch.Competitor?.StartTime is DateTime st ? st : "", // IFormattable when present
        "CompetitorIdType" or "competitoridtype" => punch.CompetitorIdType,
        "Control" or "control" => punch.Control,
        "ControlType" or "controltype" => punch.ControlType,
        "Type" or "type" => punch.ControlTypeShort ?? "",
        "Time" or "time" => punch.Time,
        "Source" or "source" => punch.SourceId,
        "Cancellation" or "cancellation" => punch.Cancellation ? "ANN" : "",
        "Status" or "status" => punch.CompetitorStatus == CompetitorStatus.Unknown ? "" : punch.CompetitorStatus.ToString(),
        "NetTime" or "nettime" => punch.NetTime,
        "ReceivedAt" or "receivedat" => punch.ReceivedAt,
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

    public static bool UsesPlaceholder(string format, string key)
    {
      return FormatterRegex().Matches(format)
        .Any(match => string.Equals(match.Groups[1].Value, key, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\{([^\{\}]+?)(?:\:([^\{\}]*))?\}", RegexOptions.Multiline)]
    private static partial Regex FormatterRegex();
  }
}

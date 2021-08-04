using RadioSender.Hosts.Common;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioSender.Helpers
{
  public static class FormatStringHelper
  {

    static object MapKey(Punch punch, string key)
    {
      return key switch
      {
        "Card" or "card" => punch.Card,
        "Control" or "control" => punch.Control,
        "ControlType" or "controltype" => punch.ControlType,
        "Time" or "time" => punch.Time,
        "UnixS" or "unixs" => new DateTimeOffset(punch.Time).ToUnixTimeSeconds(),
        "UnixMs" or "unims" => new DateTimeOffset(punch.Time).ToUnixTimeMilliseconds(),
        "CRLF" => "\r\n",
        "CR" => '\r',
        "LF" => '\n',
        _ => "",
      };
    }

    static readonly Regex FormatterPattern = new(@"\{([^\{\}]+?)(?:\:([^\{\}]*))?\}", RegexOptions.Multiline);

    public static string GetString(Punch punch, string format)
    {
      return FormatterPattern.Replace(format, (match) =>
      {
        var capture = match?.Groups?.OfType<Group>().Skip(1).Select((group) => group.Value).ToArray();
        if (!(capture?.FirstOrDefault() is string key && MapKey(punch, key) is var value && value != null))
          return match.Value;

        return capture.Length > 1 && value is IFormattable formattable ? formattable.ToString(capture[1], CultureInfo.InvariantCulture) : value?.ToString();
      });
    }

    public static byte[] GetBytes(Punch punch, string format)
    {
      return Encoding.UTF8.GetBytes(GetString(punch, format));
    }
  }
}

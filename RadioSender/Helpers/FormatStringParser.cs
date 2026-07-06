using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioSender.Helpers
{
  /// <summary>
  /// Inverse of <see cref="FormatStringHelper"/>: given the same format template
  /// (e.g. "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}") it builds a regex
  /// that extracts field values from an incoming line and rebuilds a <see cref="Punch"/>.
  /// Literals between tokens (including {CRLF}/{CR}/{LF}) act as delimiters.
  /// Only a subset of fields can be parsed back; unknown/unparseable tokens are
  /// treated as opaque text that must still match, but their value is ignored.
  /// </summary>
  public sealed partial class FormatStringParser
  {
    private readonly Regex _regex;
    private readonly IReadOnlyList<Token> _tokens;

    private readonly record struct Token(string Key, string? Format, string GroupName);

    private FormatStringParser(Regex regex, IReadOnlyList<Token> tokens)
    {
      _regex = regex;
      _tokens = tokens;
    }

    /// <summary>
    /// Builds a parser from a format string. Returns null if the format has no
    /// parseable field (a template made only of literals can never yield a punch).
    /// </summary>
    public static FormatStringParser? TryCreate(string format)
    {
      if (string.IsNullOrWhiteSpace(format))
        return null;

      var pattern = new StringBuilder();
      var tokens = new List<Token>();
      var groupIndex = 0;
      var consumed = 0; // index in `format` up to which we've emitted pattern

      foreach (Match match in TokenRegex().Matches(format))
      {
        // literal text before this token
        AppendLiteral(pattern, format[consumed..match.Index]);

        var key = match.Groups[1].Value;
        var fmt = match.Groups[2].Success ? match.Groups[2].Value : null;

        if (IsLineTerminatorToken(key))
        {
          // {CRLF}/{CR}/{LF}: the reader already strips line terminators, so match
          // any (possibly empty) run of whitespace here instead of requiring \r\n.
          pattern.Append(@"\s*");
        }
        else
        {
          // Every other token becomes a capture group. Fields we can map back
          // (CompetitorId/Control/Time/...) are decoded; the rest (Source/Name/...)
          // are captured only so the surrounding literals still line up, then ignored.
          var groupName = "f" + groupIndex++;
          tokens.Add(new Token(key, fmt, groupName));
          pattern.Append('(').Append("?<").Append(groupName).Append('>').Append(FieldSubPattern(key, fmt)).Append(')');
        }

        consumed = match.Index + match.Length;
      }

      // trailing literal
      AppendLiteral(pattern, format[consumed..]);

      if (tokens.Count == 0)
        return null;

      // Anchor at start; allow trailing delimiters/whitespace so a single line matches.
      var regex = new Regex("^" + pattern + @"\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      return new FormatStringParser(regex, tokens);
    }

    /// <summary>
    /// Parses one line into a Punch. Returns null when the line does not match the
    /// format or a required value cannot be interpreted (bad time, non-numeric card).
    /// </summary>
    public Punch? TryParse(string line, string sourceId)
    {
      var match = _regex.Match(line);
      if (!match.Success)
        return null;

      string competitorId = "";
      var idType = CompetitorIdType.Unknown;
      int control = 0;
      var controlType = PunchControlType.Unknown;
      var status = CompetitorStatus.Unknown;
      bool cancellation = false;
      DateTime? time = null;
      bool sawId = false, sawControl = false, sawTime = false;

      foreach (var token in _tokens)
      {
        var value = match.Groups[token.GroupName].Value;

        switch (token.Key.ToLowerInvariant())
        {
          case "competitorid":
            competitorId = value.Trim();
            sawId = true;
            break;
          case "bib":
            competitorId = value.Trim();
            idType = CompetitorIdType.BibNumber;
            sawId = true;
            break;
          case "card":
          case "card2":
            competitorId = value.Trim();
            idType = CompetitorIdType.PunchingCard;
            sawId = true;
            break;
          case "control":
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out control))
              return null;
            sawControl = true;
            break;
          case "type":
          case "controltype":
            controlType = ParseControlType(value.Trim());
            break;
          case "status":
            status = ParseStatus(value.Trim());
            break;
          case "cancellation":
            cancellation = !string.IsNullOrWhiteSpace(value) &&
                           !value.Trim().Equals("0", StringComparison.Ordinal);
            break;
          case "time":
            if (!TryParseTime(value.Trim(), token.Format, out var dt))
              return null;
            time = dt;
            sawTime = true;
            break;
          // Source/Name/Class/etc. are matched to satisfy the pattern but not mapped back.
          default:
            break;
        }
      }

      if (!sawId || string.IsNullOrEmpty(competitorId))
        return null;

      var punchTime = time ?? DateTime.Now;

      // Decode the special status times written by the Tcp target
      // (00:00:01=DNS .. 00:00:05=OverTime). Only when Time was actually parsed.
      if (sawTime && status == CompetitorStatus.Unknown && punchTime.TimeOfDay.Milliseconds == 0)
      {
        var totalSeconds = (int)punchTime.TimeOfDay.TotalSeconds;
        var mapped = totalSeconds switch
        {
          1 => CompetitorStatus.DNS,
          2 => CompetitorStatus.DNF,
          3 => CompetitorStatus.MP,
          4 => CompetitorStatus.DSQ,
          5 => CompetitorStatus.OverTime,
          _ => CompetitorStatus.Unknown
        };
        if (mapped != CompetitorStatus.Unknown)
        {
          status = mapped;
          punchTime = DateTime.Now;
        }
      }

      // If the format never carried a control but the type says finish, keep default 0.
      _ = sawControl;

      return new Punch(
        CompetitorId: competitorId,
        Time: punchTime,
        Control: control,
        SourceId: sourceId,
        ReceivedAt: DateTimeOffset.UtcNow,
        CompetitorIdType: idType,
        ControlType: controlType,
        CompetitorStatus: status,
        Cancellation: cancellation
      );
    }

    private static bool TryParseTime(string value, string? format, out DateTime result)
    {
      result = default;
      if (string.IsNullOrWhiteSpace(value))
        return false;

      // Try the exact format first (comma-decimals like HH:mm:ss,fff are handled by DateTime).
      if (!string.IsNullOrWhiteSpace(format) &&
          DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        return ToTodayIfNoDate(exact, format, out result);

      // Fall back to a lenient parse (covers formats we can't perfectly round-trip).
      if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lenient))
      {
        result = lenient;
        return true;
      }

      return false;
    }

    private static bool ToTodayIfNoDate(DateTime parsed, string format, out DateTime result)
    {
      // If the format only carried a time-of-day (no date tokens), the parsed date
      // defaults to 0001-01-01; attach today's local date instead.
      var hasDate = format.IndexOfAny(['y', 'M', 'd']) >= 0;
      if (!hasDate)
      {
        var now = DateTime.Now;
        result = new DateTime(now.Year, now.Month, now.Day) + parsed.TimeOfDay;
      }
      else
      {
        result = parsed;
      }
      return true;
    }

    private static PunchControlType ParseControlType(string value) => value.ToUpperInvariant() switch
    {
      "CN" or "CONTROL" => PunchControlType.Control,
      "FIN" or "FINISH" => PunchControlType.Finish,
      "CLR" or "CLEAR" => PunchControlType.Clear,
      "CHK" or "CHECK" => PunchControlType.Check,
      "STA" or "START" => PunchControlType.Start,
      _ => PunchControlType.Unknown
    };

    private static CompetitorStatus ParseStatus(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return CompetitorStatus.Unknown;
      return Enum.TryParse<CompetitorStatus>(value, ignoreCase: true, out var s) ? s : CompetitorStatus.Unknown;
    }

    // A per-token sub-pattern. {Control} is always a number in the source formatter,
    // so it requires at least one digit. Every other field matches lazily and MAY be
    // empty (.*?): the Tcp target emits "" for absent {Bib}/{Card}/{Type}/{Status}/
    // {Cancellation}/{Name}/... so an empty capture must still round-trip.
    private static string FieldSubPattern(string key, string? format) => key.ToLowerInvariant() switch
    {
      "control" => @"-?\d+",
      _ => ".*?"
    };

    private static bool IsLineTerminatorToken(string key) =>
      key is "CRLF" or "CR" or "LF";

    private static void AppendLiteral(StringBuilder pattern, string literal)
    {
      if (literal.Length > 0)
        pattern.Append(Regex.Escape(literal));
    }

    [GeneratedRegex(@"\{([^\{\}]+?)(?:\:([^\{\}]*))?\}", RegexOptions.Multiline)]
    private static partial Regex TokenRegex();
  }
}

using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace RadioSender.Hosts.Target.OResults
{
  // Body of POST /punches/external (https://api.oresults.eu/api-docs)
  public record OResultsRequest(
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("records")] IReadOnlyList<OResultsPunch> Records);

  public record OResultsPunch(
    [property: JsonPropertyName("card")] long Card,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("punch_type")] int? PunchType);

  public static class OResultsService
  {
    public static OResultsPunch? Encode(Punch punch, bool useUtc, bool ignoreCompetitorIdType) => ToRecord(punch, useUtc, ignoreCompetitorIdType);

    private static OResultsPunch? ToRecord(Punch punch, bool useUtc, bool ignoreCompetitorIdType)
    {
      // OResults does not support cancellations
      if (punch.Cancellation)
        return null;

      // OResults wants the SI card number. Prefer the card resolved by enrichment;
      // fall back to CompetitorId only when no specific card is available
      // (valid only if the punch itself is a punching card, unless the type check is disabled).
      var cardStr = punch.Competitor?.Card
        ?? (ignoreCompetitorIdType || punch.CompetitorIdType is CompetitorIdType.PunchingCard or CompetitorIdType.Unknown ? punch.CompetitorId : null);

      if (!long.TryParse(cardStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var card))
      {
        Log.Warning("OResults requires a numeric card number, got competitorId '{competitorId}' (type {type}) with no enriched card. Ignored",
          punch.CompetitorId, punch.CompetitorIdType);
        return null;
      }

      // OResults punch_type: 0=Control, 1=Start, 2=Clear, 3=Check, 9=Finish
      int? punchType = punch.ControlType switch
      {
        PunchControlType.Control => 0,
        PunchControlType.Start => 1,
        PunchControlType.Clear => 2,
        PunchControlType.Check => 3,
        PunchControlType.Finish => 9,
        _ => null,
      };

      // RFC3339: UTC with 'Z', or event-local time without offset
      var time = useUtc
        ? punch.Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        : punch.Time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

      return new OResultsPunch(card, punch.Control, time, punchType);
    }

  }
}

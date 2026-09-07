using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RadioSender.Hosts.Enrichment.Oribos
{
  // Resolved competitor data kept in the lookup maps.
  public record OribosEntry(
    string Bib, string? Card, string? Card2, string? Name, string? Class,
    string? Nation, string? ClubId, string? ClubName, string? ClubNation,
    DateTime? StartTime, string Status, DateTime? FinishTime = null, bool SubJudice = false);

  public static class OribosService
  {
    private const double StartBeforeRaceStartThresholdSeconds = 3600 * 11;
    private const double StartBeforeRaceStartModuloSeconds = 3600 * 12;

    // Oribos status codes that mean "no longer racing" — used to disambiguate a card/bib
    // shared by more than one competitor.
    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
      { "CL", "NP", "SQ", "RI", "FT", "PE", "PM", "DI" };

    // Pure resolution against the given lookup maps (static for testability).
    // For an unknown id type, the map that resolves the entry also tells us what the
    // id was: a card-map hit means it's a punching card, a bib-map hit means it's a
    // bib. Downstream targets (e.g. Oribos) route on CompetitorIdType, so we set it
    // here so a source that emits {CompetitorId} (Unknown type) is still routable.
    public static Punch Enrich(
      Punch punch,
      IReadOnlyDictionary<string, OribosEntry> cardMap,
      IReadOnlyDictionary<string, OribosEntry> bibMap)
    {
      OribosEntry? entry;
      var resolvedIdType = punch.CompetitorIdType;

      switch (punch.CompetitorIdType)
      {
        case CompetitorIdType.PunchingCard:
          entry = Lookup(cardMap, punch.CompetitorId);
          break;
        case CompetitorIdType.BibNumber:
          entry = Lookup(bibMap, punch.CompetitorId);
          break;
        default:
          // unknown id type: try card first, then bib (best-effort)
          entry = Lookup(cardMap, punch.CompetitorId);
          if (entry != null)
            resolvedIdType = CompetitorIdType.PunchingCard;
          else if ((entry = Lookup(bibMap, punch.CompetitorId)) != null)
            resolvedIdType = CompetitorIdType.BibNumber;
          break;
      }

      if (entry == null)
        return punch; // best-effort: pass through unchanged, no log

      return punch with { Competitor = ToCompetitor(entry), CompetitorIdType = resolvedIdType };
    }

    private static Competitor ToCompetitor(OribosEntry e) => new(
      Bib: e.Bib,
      Card: e.Card,
      Card2: e.Card2,
      Name: e.Name,
      Class: e.Class,
      Nation: e.Nation,
      ClubId: e.ClubId,
      ClubName: e.ClubName,
      ClubNation: e.ClubNation,
      StartTime: e.StartTime);

    private static OribosEntry? Lookup(IReadOnlyDictionary<string, OribosEntry> map, string? key)
      => key != null && map.TryGetValue(key, out var e) ? e : null;


    // Builds card→entry and bib→entry maps. A competitor's Card and Card2 both index to it.
    // Keys shared by more than one competitor are disambiguated by status (keep the only one
    // still racing); if still ambiguous the key is dropped and a one-time warning is logged.
    // Returns the maps and the count of ambiguous keys dropped.
    public static (IReadOnlyDictionary<string, OribosEntry> cardMap,
                   IReadOnlyDictionary<string, OribosEntry> bibMap,
                   int ambiguousCount) BuildLookups(OrServer data, HashSet<string>? warnedKeys = null)
    {
      var competitors = data.Competitors?.Where(c => c.Bib != null).ToList() ?? [];

      // clubId → club name (ClubId matches OrClub.CountryId)
      var clubsById = new Dictionary<string, string>();
      foreach (var club in data.Clubs ?? [])
      {
        if (!string.IsNullOrWhiteSpace(club.CountryId) && !string.IsNullOrWhiteSpace(club.Name))
          clubsById[club.CountryId] = club.Name;
      }

      // bib → entry
      var bibCandidates = new Dictionary<string, List<OrCompetitor>>();
      foreach (var c in competitors)
      {
        var bib = c.Bib!.Value.ToString(CultureInfo.InvariantCulture);
        if (!bibCandidates.TryGetValue(bib, out var list))
          bibCandidates[bib] = list = [];
        list.Add(c);
      }

      // card → entry (Card and Card2)
      var cardCandidates = new Dictionary<string, List<OrCompetitor>>();
      foreach (var c in competitors)
      {
        foreach (var card in new[] { c.Card, c.Card2 })
        {
          if (card == null || card.Value <= 0)
            continue;
          var key = card.Value.ToString(CultureInfo.InvariantCulture);
          if (!cardCandidates.TryGetValue(key, out var list))
            cardCandidates[key] = list = [];
          list.Add(c);
        }
      }

      var ambiguous = 0;
      var cardMap = Resolve(cardCandidates, data.Race.Startutc, clubsById, "card", warnedKeys, ref ambiguous);
      var bibMap = Resolve(bibCandidates, data.Race.Startutc, clubsById, "bib", warnedKeys, ref ambiguous);

      return (cardMap, bibMap, ambiguous);
    }

    private static IReadOnlyDictionary<string, OribosEntry> Resolve(
      Dictionary<string, List<OrCompetitor>> candidates,
      DateTimeOffset startutc,
      IReadOnlyDictionary<string, string> clubsById,
      string keyKind,
      HashSet<string>? warnedKeys,
      ref int ambiguous)
    {
      var map = new Dictionary<string, OribosEntry>();

      foreach (var (key, list) in candidates)
      {
        OrCompetitor? chosen;
        if (list.Count == 1)
        {
          chosen = list[0];
        }
        else
        {
          // disambiguate: keep only those still racing / to start
          var stillRacing = list.Where(c => !FinishedStatuses.Contains(c.Status ?? "")).ToList();
          chosen = stillRacing.Count == 1 ? stillRacing[0] : null;
        }

        if (chosen == null)
        {
          ambiguous++;
          if (warnedKeys != null && warnedKeys.Add($"{keyKind}:{key}"))
            Log.Warning("Oribos: ambiguous {kind} {key} maps to multiple racing competitors; not mapped", keyKind, key);
          continue;
        }

        // a previously-ambiguous key resolved → allow warning again if it recurs
        warnedKeys?.Remove($"{keyKind}:{key}");
        map[key] = ToEntry(chosen, startutc, clubsById);
      }

      return map;
    }

    private static OribosEntry ToEntry(OrCompetitor c, DateTimeOffset startutc, IReadOnlyDictionary<string, string> clubsById)
    {
      var bib = c.Bib!.Value.ToString(CultureInfo.InvariantCulture);
      var card = c.Card is int cv && cv > 0 ? cv.ToString(CultureInfo.InvariantCulture) : null;
      var card2 = c.Card2 is int cv2 && cv2 > 0 ? cv2.ToString(CultureInfo.InvariantCulture) : null;
      var fullName = string.Join(" ", new[] { c.Name, c.Surname }.Where(s => !string.IsNullOrWhiteSpace(s)));

      string? clubName = null;
      if (!string.IsNullOrWhiteSpace(c.ClubId) && clubsById.TryGetValue(c.ClubId, out var n))
        clubName = n;

      return new OribosEntry(
        Bib: bib,
        Card: card,
        Card2: card2,
        Name: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
        Class: c.Class,
        Nation: string.IsNullOrWhiteSpace(c.Naz) ? null : c.Naz,
        ClubId: string.IsNullOrWhiteSpace(c.ClubId) ? null : c.ClubId,
        ClubName: clubName,
        ClubNation: string.IsNullOrWhiteSpace(c.ClubCountry) ? null : c.ClubCountry,
        StartTime: AbsoluteStart(startutc, c.Start),
        Status: c.Status ?? "",
        FinishTime: AbsoluteFinish(startutc, c.Finish),
        SubJudice: c.Sj);
    }

    // Oribos Start is seconds relative to race start; values above 11h are wrapped (start
    // before race "hour 0"). Returns absolute local time, or null when not set.
    public static DateTime? AbsoluteStart(DateTimeOffset startutc, double start)
    {
      if (start <= 0)
        return null;
      return (startutc + TimeSpan.FromSeconds(NormalizeRelativeStart(start))).LocalDateTime;
    }

    public static double NormalizeRelativeStart(double start)
      => start > StartBeforeRaceStartThresholdSeconds ? start - StartBeforeRaceStartModuloSeconds : start;

    // Oribos Finish is seconds relative to race start; finishes are always after race
    // start, so no 12h wrapping applies. Returns absolute local time, or null when not set.
    public static DateTime? AbsoluteFinish(DateTimeOffset startutc, double finish)
    {
      if (finish <= 0)
        return null;
      return (startutc + TimeSpan.FromSeconds(finish)).LocalDateTime;
    }

    // Oribos status code → RadioSender CompetitorStatus. Null = ignored (no enum / no event).
    public static CompetitorStatus? MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
      "PE" or "PM" => CompetitorStatus.MP,
      "NP" => CompetitorStatus.DNS,
      "SQ" => CompetitorStatus.DSQ,
      "RI" => CompetitorStatus.DNF,
      "FT" => CompetitorStatus.OverTime,
      "CL" => CompetitorStatus.OK,
      "GA" => CompetitorStatus.Running,
      "IP" => CompetitorStatus.WaitingStart,
      _ => null,
    };

    // "Anomalous" outcomes: emitted whenever newly detected. Their later corrections
    // (back to CL with the official time, or reset to IP/GA) are emitted too — see
    // EvaluateTransition.
    private static readonly HashSet<CompetitorStatus> EmittableStatuses =
      [CompetitorStatus.MP, CompetitorStatus.DNS, CompetitorStatus.DSQ, CompetitorStatus.DNF, CompetitorStatus.OverTime];

    // A status already broadcast to targets as a final outcome: a later change away
    // from it must be propagated downstream as a correction.
    private static bool IsFinalOutcome(CompetitorStatus status)
      => status == CompetitorStatus.OK || EmittableStatuses.Contains(status);

    // Decides whether a prev→new status change must be emitted and with which status,
    // and whether the punch must carry the official finish time:
    // - → PM/PE/NP/SQ/RI/FT (anomalous): always emitted (also when prev is unknown)
    // - PM/PE/... → CL: emitted as OK with the finish time; for targets a regular time
    //   means "correctly classified with this time" and overwrites the anomalous status.
    //   Also emitted when prev is unknown (bib first seen sub judice, e.g. the service
    //   started during a review: the confirmed classification must still reach targets)
    // - CL/PM/PE/... → IP/GA: result voided, competitor back to start/course, emitted so
    //   targets reset; the normal pre-arrival IP→GA progression is never emitted
    public static (CompetitorStatus status, bool useFinishTime)? EvaluateTransition(string? prevStatus, string? newStatus)
    {
      if (MapStatus(newStatus) is not CompetitorStatus status)
        return null;

      if (EmittableStatuses.Contains(status))
        return (status, false);

      var prev = MapStatus(prevStatus);

      if (status == CompetitorStatus.OK)
      {
        // suppressed only for the normal arrival (GA/IP → CL): that time already
        // flowed to the targets through the regular punches
        var normalArrival = prev is CompetitorStatus p && !EmittableStatuses.Contains(p);
        return normalArrival ? null : (status, true);
      }

      if (prev is not CompetitorStatus prevOutcome || !IsFinalOutcome(prevOutcome))
        return null;

      if (status is CompetitorStatus.Running or CompetitorStatus.WaitingStart)
        return (status, false);

      return null;
    }

    // Computes the next status snapshot and the status changes to emit.
    // Sub judice entries are frozen: the snapshot keeps their last confirmed status and
    // nothing is emitted for them; once the flag is cleared the transition is evaluated
    // against that confirmed status. A CL reached without a finish time in the feed is
    // also kept pending, so the correction is retried when the time appears.
    public static (Dictionary<string, string> snapshot, List<(OribosEntry entry, CompetitorStatus status, bool useFinishTime)> toEmit)
      ComputeStatusChanges(
        IReadOnlyDictionary<string, OribosEntry> bibMap,
        IReadOnlyDictionary<string, string> prevSnapshot,
        bool initialized)
    {
      var snapshot = new Dictionary<string, string>();
      var toEmit = new List<(OribosEntry, CompetitorStatus, bool)>();

      foreach (var (bib, entry) in bibMap)
      {
        if (entry.SubJudice)
        {
          if (prevSnapshot.TryGetValue(bib, out var confirmed))
            snapshot[bib] = confirmed;
          continue;
        }

        snapshot[bib] = entry.Status;

        if (!initialized)
          continue; // first fetch: just initialize, never emit

        prevSnapshot.TryGetValue(bib, out var prev);
        if (prev == entry.Status)
          continue;

        if (EvaluateTransition(prev, entry.Status) is not { } transition)
          continue;

        if (transition.useFinishTime && entry.FinishTime == null)
        {
          // keep the previous status (or none) so the correction is retried on the
          // next fetch, once the finish time appears in the feed
          if (prev != null)
            snapshot[bib] = prev;
          else
            snapshot.Remove(bib);
          continue;
        }

        toEmit.Add((entry, transition.status, transition.useFinishTime));
      }

      return (snapshot, toEmit);
    }


  }
}

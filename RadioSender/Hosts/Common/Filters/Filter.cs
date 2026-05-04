using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Hosts.Common.Filters
{
  public record Filter : IFilter
  {
    public static Filter Invariant { get => new() { Enable = false }; }

    public string Name { get; init; } = null!;
    public bool Enable { get; init; } = true;
    public HashSet<int> IncludeOnlyControls { get; init; } = new HashSet<int>();
    public HashSet<string> IncludeOnlyCompetitorIds { get; init; } = new HashSet<string>();
    [Obsolete("Use IncludeOnlyCompetitorIds instead")]
    public HashSet<string> IncludeOnlyCards { get; init; } = new HashSet<string>();
    public Dictionary<string, int> MapControls { get; init; } = new();
    public Dictionary<string, string> MapCompetitorIds { get; init; } = new();
    [Obsolete("Use MapCompetitorIds instead")]
    public Dictionary<string, string> MapCards { get; init; } = new();
    public Dictionary<PunchControlType, HashSet<int>> TypeFromCode { get; init; } = new();
    public CompetitorIdType? OverrideCompetitorIdType { get; init; }
    public TimeSpan IgnoreOlderThan { get; init; }

    public Punch? Transform(Punch? punch)
    {
      if (!Enable || punch == null)
        return punch;

      if (!punch.NetTime && IgnoreOlderThan != default && DateTime.Now - punch.Time > IgnoreOlderThan)
        return null;

      var controlBeforeFilter_string = punch.Control.ToString();

      var controlBeforeFilter_int = int.Parse(controlBeforeFilter_string);

      var control = MapControls.ContainsKey(controlBeforeFilter_string) ? MapControls[controlBeforeFilter_string] : punch.Control;

      // 0 is a valid code for some systems
      // 0 is discarded only when came from the filter
      if (
        (controlBeforeFilter_int != 0 && control <= 0) ||
        (IncludeOnlyControls.Count != 0 && !IncludeOnlyControls.Contains(control)))
      {
        return null; // discard
      }

#pragma warning disable CS0618 // Type or member is obsolete
      var mapCompetitorIds = MapCompetitorIds.Count != 0 ? MapCompetitorIds : MapCards;
      var includeOnlyCompetitorIds = IncludeOnlyCompetitorIds.Count != 0 ? IncludeOnlyCompetitorIds : IncludeOnlyCards;
#pragma warning restore CS0618 // Type or member is obsolete

      var competitorId = mapCompetitorIds.TryGetValue(punch.CompetitorId, out var mapped)
                        ? mapped
                        : punch.CompetitorId;

      if (string.IsNullOrEmpty(competitorId) || (includeOnlyCompetitorIds.Count != 0 && !includeOnlyCompetitorIds.Contains(competitorId)))
      {
        return null; // discard
      }

      var ctype = punch.ControlType;
      if (ctype == PunchControlType.Unknown)
      {
        ctype = PunchControlType.Control;
        foreach (var type in TypeFromCode)
        {
          if (type.Value.Contains(control))
          {
            ctype = type.Key;
            break;
          }
        }
      }

      return punch with
      {
        CompetitorId = competitorId,
        CompetitorIdType = OverrideCompetitorIdType ?? punch.CompetitorIdType,
        Control = control,
        ControlType = ctype,
      };
    }

    public IEnumerable<Punch> Transform(IEnumerable<Punch>? punches)
    {
      if (punches == null)
        return Array.Empty<Punch>();

      if (!Enable || !punches.Any())
        return punches;

      return punches.Select(p => Transform(p)).Where(p => p != null).Select(p => p!);
    }

  }

  public static class IEnumerableFilterExtension
  {
    public static IFilter GetFilter(this IEnumerable<IFilter> filters, string? name)
    {
      // filter name null = no filter (invariant)

      if (!string.IsNullOrWhiteSpace(name) && filters.Any(f => f.Name == name))
        return filters.First(f => f.Name == name);

      return Filter.Invariant;
    }
  }
}

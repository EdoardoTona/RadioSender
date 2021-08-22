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
    public HashSet<string> IncludeOnlyCards { get; init; } = new HashSet<string>();
    public Dictionary<string, int> MapControls { get; init; } = new();
    public Dictionary<string, string> MapCards { get; init; } = new();
    public Dictionary<PunchControlType, HashSet<int>> TypeFromCode { get; init; } = new();
    public TimeSpan IgnoreOlderThan { get; init; }

    public Punch? Transform(Punch? punch)
    {
      if (!Enable || punch == null)
        return punch;

      if (IgnoreOlderThan != default && DateTime.Now - punch.Time > IgnoreOlderThan)
        return null;

      var control = MapControls.ContainsKey(punch.Control.ToString()) ? MapControls[punch.Control.ToString()] : punch.Control;

      if (control == 0 || (IncludeOnlyControls.Count != 0 && !IncludeOnlyControls.Contains(control)))
      {
        return null; // discard
      }

      var card = MapCards.ContainsKey(punch.Card) ? MapCards[punch.Card] : punch.Card;

      if (string.IsNullOrEmpty(card) || (IncludeOnlyCards.Count != 0 && !IncludeOnlyCards.Contains(card)))
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
        Card = card,
        Control = control,
        ControlType = ctype,
      }; ;
    }

    public IEnumerable<Punch> Transform(IEnumerable<Punch> punches)
    {
      if (!Enable)
        return punches;

      if (punches == null || !punches.Any())
        return Array.Empty<Punch>();

      return punches.Select(p => Transform(p)).Where(p => p != null).Select(p => p!);
    }
  }

  public static class IEnumerableFilterExtension
  {
    public static IFilter GetFilter(this IEnumerable<IFilter> filters, string? name)
    {
      if (!string.IsNullOrWhiteSpace(name) && filters.Any(f => f.Name == name))
        return filters.Where(f => f.Name == name).First();

      return Filter.Invariant;
    }
  }
}

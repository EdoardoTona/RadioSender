using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Liveresults.Models
{
  public record ResultItem
  {
    public float Order { get; set; }
    public string Position { get; set; }
    public string Name { get; set; }
    public string Club { get; set; }
    public string Country { get; set; }
    public DateTimeOffset StartDTO { get; set; }
    public string Start { get; set; }
    public Dictionary<int, string> Intermediates { get; set; } = new(); // TODO list
    public Dictionary<int, int> IntermediatesTimes { get; set; } = new(); // TODO list
    public Dictionary<int, int> IntermediatesPositions { get; set; } = new(); // TODO list
    public int TotalTime { get; set; }
    public int TotalPosition { get; set; }
    public string Total { get; set; }
    public string Status { get; set; }
    public bool LastUpdated { get; set; }
    public bool SJ { get; set; }
  }

  public struct Time
  {
    public TimeSpan Value { get; set; }
    public TimeSpan Difference { get; set; }
    public int? Position { get; set; }
  }

  public enum Status
  {
    InRace,
    OK,
    DNS,
    DNF,
    MP,
    WP,
    OT
  }
}

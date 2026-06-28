using System.Collections.Generic;

namespace RadioSender.Hosts.Common.Filters
{
  public interface IFilter
  {
    string Name { get; init; }
    IReadOnlyList<string> Enrichers { get; }
    Punch? Transform(Punch? punch);
    IEnumerable<Punch> Transform(IEnumerable<Punch>? punches);
  }
}

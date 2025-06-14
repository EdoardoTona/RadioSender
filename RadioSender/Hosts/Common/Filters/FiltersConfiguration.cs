using System.Collections.Generic;

namespace RadioSender.Hosts.Common.Filters
{
  public class FiltersConfiguration
  {
    public IEnumerable<Filter> List { get; set; } = [];
  }
}

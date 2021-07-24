using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common.Filters
{
  public interface IFilter
  {
    string Name { get; init; }
    Punch Transform(Punch punch);
    IEnumerable<Punch> Transform(IEnumerable<Punch> punches);
  }
}

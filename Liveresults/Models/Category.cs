using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Liveresults.Models
{
  public record Category
  {
    public string Name { get; set; }
    public string ShortName { get; set; }
    public IEnumerable<Intermediate> Intermediates { get; set; }
    public int Leg { get; set; }
    public int Legs { get; set; }
  }

  public struct Intermediate
  {
    public string Name { get; set; }
    public int Code { get; set; }
  }

}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record RxMsg
  {
    public RxHeader Header { get; set; }
  }
}

using RadioSender.Hosts.Common;
using RadioSender.Hosts.Target.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public interface IDeviceHub
  {
    Task Log(string time, int level, string message, Exception? exception);
    Task Punch(Punch? punch);
    Task Punches(IEnumerable<Punch> punches);
    Task Graph(List<VisEdge>? edges, List<VisNode>? nodes);
  }
}

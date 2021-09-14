using Common;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Target.UI;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public interface IDeviceHub
  {
    Task Abort();
    Task Connection(ConnectedClient client);
    Task Connections(IEnumerable<ConnectedClient> clients);
    Task Log(LogMessage logMessage);
    Task Punch(Punch? punch);
    Task Punches(IEnumerable<Punch> punches);
    Task Graph(List<VisEdge>? edges, List<VisNode>? nodes);
  }
}

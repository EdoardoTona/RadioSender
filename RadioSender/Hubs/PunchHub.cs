using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public class PunchHub : Hub
  {
    public async Task SendPunch(Punch punch)
    {
      await Clients.All.SendAsync("Punch", punch);
    }
  }
}
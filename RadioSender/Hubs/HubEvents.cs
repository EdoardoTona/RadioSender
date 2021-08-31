using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public class HubEvents
  {
    public delegate void GroupJoinedEvent(HubCallerContext context, string group);

    public event GroupJoinedEvent? GroupJoined;

    public void JoinGroup(HubCallerContext context, string group) => GroupJoined?.Invoke(context, group);
  }
}

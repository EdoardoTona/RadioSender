using Microsoft.AspNetCore.SignalR;

namespace Liveresults.Hubs
{
  public class HubEvents
  {
    public delegate void GroupJoinedHandler(HubCallerContext context, string group);

    public event GroupJoinedHandler? GroupJoined;
    public void JoinGroup(HubCallerContext context, string group) => GroupJoined?.Invoke(context, group);


    public delegate void ConnectionAbortedHandler(HubCallerContext context);

    public event ConnectionAbortedHandler? ConnectionAborted;
    public void AbortConnection(HubCallerContext context) => ConnectionAborted?.Invoke(context);

  }
}

using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public class DeviceHub : Hub<IDeviceHub>
  {
    public const string GROUP_LOG = "log";
    public const string GROUP_GRAPH = "graph";
    public const string GROUP_PUNCHES = "punches";

    private readonly HubEvents _hubEvents;
    public DeviceHub(HubEvents hubEvents)
    {
      _hubEvents = hubEvents;
    }

    public void SetMetadata(Dictionary<string, object> value)
    {
      foreach (var i in value)
        Context.Items.Add(i.Key, i.Value);
    }

    public void JoinGroup(string group)
    {
      _hubEvents.JoinGroup(Context, group);
    }

    public override Task OnDisconnectedAsync(Exception? e)
    {
      return base.OnDisconnectedAsync(e);
    }

    public override Task OnConnectedAsync()
    {
      return base.OnConnectedAsync();
    }
  }
}
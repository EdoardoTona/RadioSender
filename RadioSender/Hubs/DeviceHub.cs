using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
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
    public const string GROUP_STATS = "stats";

    private readonly DispatcherService _dispatcher;
    private readonly HubEvents _hubEvents;
    public DeviceHub(HubEvents hubEvents, DispatcherService dispatcher)
    {
      _hubEvents = hubEvents;
      _dispatcher = dispatcher;
    }

    public void JoinGroup(string group, Dictionary<string, object> metadata)
    {
      foreach (var i in metadata)
        Context.Items[i.Key] = i.Value;

      _hubEvents.JoinGroup(Context, group);
    }

    public void Abort(string id)
    {
      Clients.Client(id).Abort();
    }

    public void Ping()
    {
        _dispatcher.Ping();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
      base.OnDisconnectedAsync(exception);

      _hubEvents.AbortConnection(Context);

      return Task.CompletedTask;
    }

    public override Task OnConnectedAsync()
    {
      base.OnConnectedAsync();
      return Task.CompletedTask;
    }
  }
}
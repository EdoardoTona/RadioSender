using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Liveresults.Hubs
{
  public class ResultsHub : Hub<IResultsHub>
  {

    public const string GROUP_CATEGORIES = "categories";
    public const string GROUP_RESULTS = "results";
    private readonly HubEvents _hubEvents;

    public ResultsHub(HubEvents hubEvents)
    {
      _hubEvents = hubEvents;
    }

    public void JoinGroup(string group, Dictionary<string, object>? metadata = null)
    {
      if (metadata != null)
        foreach (var i in metadata)
          Context.Items[i.Key] = i.Value;

      _hubEvents.JoinGroup(Context, group);
    }

    public void Abort(string id)
    {
      Clients.Client(id).Abort();
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
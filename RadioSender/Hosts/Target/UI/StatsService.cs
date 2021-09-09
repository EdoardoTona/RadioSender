using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.UI
{
  public record ConnectedClient(string Id, string Group, string? Ip, string? UserAgent);
  public class StatsService : IHostedService
  {
    private readonly IHubContext<DeviceHub, IDeviceHub> _hubContext;
    private readonly HubEvents _hubEvents;

    private static readonly Dictionary<string, ConnectedClient> _connections = new();

    public StatsService(
      IHubContext<DeviceHub, IDeviceHub> hubContext,
      HubEvents hubEvents)
    {
      _hubContext = hubContext;
      _hubEvents = hubEvents;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _hubEvents.GroupJoined += HubEvents_GroupJoined;
      _hubEvents.ConnectionAborted += HubEvents_ConnectionAborted;

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _hubEvents.GroupJoined -= HubEvents_GroupJoined;
      _hubEvents.ConnectionAborted -= HubEvents_ConnectionAborted;

      return Task.CompletedTask;
    }

    private async void HubEvents_ConnectionAborted(HubCallerContext sender)
    {
      try
      {
        if (_hubContext == null) return;
        _connections.Remove(sender.ConnectionId);

        var connections = _connections.Select(c => c.Value);
        await _hubContext.Clients.Group(DeviceHub.GROUP_STATS).Connections(connections).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        // quiet
      }
      catch (Exception e)
      {
        Log.Error(e, "Exception Connection Aborted Event");
      }
    }


    private async void HubEvents_GroupJoined(HubCallerContext sender, string group)
    {
      try
      {
        if (_hubContext == null) return;

        sender.Items.TryGetValue("userAgent", out object? userAgent);


        var feature = sender.Features.Get<IHttpConnectionFeature>();

        var conn = new ConnectedClient(sender.ConnectionId, group, feature.RemoteIpAddress?.MapToIPv4().ToString(), userAgent?.ToString());
        _connections.Add(sender.ConnectionId, conn);
        await _hubContext.Clients.Group(DeviceHub.GROUP_STATS).Connection(conn).ConfigureAwait(false);

        if (group != DeviceHub.GROUP_STATS) return;

        try
        {
          var connections = _connections.Select(c => c.Value);
          await _hubContext.Clients.Client(sender.ConnectionId).Connections(connections).ConfigureAwait(false);
        }
        catch
        {
          Log.Error("Exception loading LOG history");
        }

        await _hubContext.Groups.AddToGroupAsync(sender.ConnectionId, DeviceHub.GROUP_STATS);
      }
      catch (OperationCanceledException)
      {
        // quiet
      }
      catch (Exception e)
      {
        Log.Error(e, "Exception Group Join");
      }
    }
  }
}

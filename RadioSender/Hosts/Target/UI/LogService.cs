using Cyotek.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.UI
{
  public class LogService(
    IHubContext<DeviceHub, IDeviceHub> hubContext,
    HubEvents hubEvents) : IHostedService
  {
    private readonly EventLogSink? _eventLogSink = EventLogSink.Instance;

    private static readonly CircularBuffer<LogMessage> history = new(1000);

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _eventLogSink?.AddHandler(CustomLogSink_NewLogEvent);
      hubEvents.GroupJoined += HubEvents_GroupJoined;

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _eventLogSink?.RemoveHandler(CustomLogSink_NewLogEvent);
      hubEvents.GroupJoined -= HubEvents_GroupJoined;

      return Task.CompletedTask;
    }

    private void CustomLogSink_NewLogEvent(object? sender, LogMessage message)
    {
      history.Put(message);

      if (hubContext == null) return;

      _ = hubContext.Clients.Group(DeviceHub.GROUP_LOG).Log(message);
    }

    private async void HubEvents_GroupJoined(HubCallerContext sender, string group)
    {
      try
      {
        if (group != DeviceHub.GROUP_LOG || hubContext == null) return;

        try
        {
          for (int i = 0; i < history.Size; i++)
          {
            sender.ConnectionAborted.ThrowIfCancellationRequested();

            var message = history.PeekAt(i);
            await hubContext.Clients.Client(sender.ConnectionId).Log(message).ConfigureAwait(false);
          }
        }
        catch
        {
          Log.Error("Exception loading LOG history");
        }

        await hubContext.Groups.AddToGroupAsync(sender.ConnectionId, DeviceHub.GROUP_LOG);
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

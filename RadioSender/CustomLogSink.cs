using Cyotek.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using RadioSender.Hubs;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;

namespace RadioSender
{
  public static class CustomLogSinkExtensions
  {
    public static LoggerConfiguration CustomLogSink(
              this LoggerSinkConfiguration loggerConfiguration,
              IFormatProvider? fmtProvider = null)
    {
      return loggerConfiguration.Sink(new CustomLogSink(fmtProvider));
    }
  }

  public class CustomLogSink : ILogEventSink
  {
    private static IFormatProvider? _formatProvider;

    public static IHubContext<DeviceHub, IDeviceHub>? HubContext { get; set; }

    private static readonly CircularBuffer<LogEvent> history = new(1000);

    public CustomLogSink(IFormatProvider? formatProvider)
    {
      _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
      history.Put(logEvent);

      if (HubContext == null) return;

      _ = HubContext.Clients.Group(DeviceHub.GROUP_LOG).Log(logEvent.Timestamp.ToString("HH:mm:ss"), (int)logEvent.Level, logEvent.RenderMessage(_formatProvider), logEvent.Exception);
    }

    public static void InitEvents(HubEvents hubEvents)
    {
      hubEvents.GroupJoined += HubEvents_GroupJoined;
    }

    private static async void HubEvents_GroupJoined(HubCallerContext sender, string group)
    {
      try
      {
        if (group != DeviceHub.GROUP_LOG || HubContext == null) return;

        try
        {
          for (int i = 0; i < history.Size; i++)
          {
            sender.ConnectionAborted.ThrowIfCancellationRequested();

            var logEvent = history.PeekAt(i);
            await HubContext.Clients.Client(sender.ConnectionId).Log(logEvent.Timestamp.ToString("HH:mm:ss"), (int)logEvent.Level, logEvent.RenderMessage(_formatProvider), logEvent.Exception).ConfigureAwait(false);
          }
        }
        catch
        {
          Log.Error("Exception loading LOG history");
        }

        await HubContext.Groups.AddToGroupAsync(sender.ConnectionId, DeviceHub.GROUP_LOG);
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error(e, "Exception Group Join");
      }
    }

  }
}

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
              IFormatProvider fmtProvider = null)
    {
      return loggerConfiguration.Sink(new CustomLogSink(fmtProvider));
    }
  }

  public class CustomLogSink : ILogEventSink
  {
    IFormatProvider _formatProvider;

    public static IHubContext<DeviceHub>? _hubContext;

    public CustomLogSink(IFormatProvider formatProvider)
    {
      _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
      if (_hubContext == null)
        return;

      _hubContext.Clients.All.SendAsync("log", logEvent.RenderMessage(_formatProvider));
    }
  }
}

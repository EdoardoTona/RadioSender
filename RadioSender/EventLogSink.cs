using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;

namespace RadioSender
{
  public static class EventLogSinkSinkExtensions
  {
    public static LoggerConfiguration EventLogSink(
              this LoggerSinkConfiguration loggerConfiguration,
              IFormatProvider? fmtProvider = null)
    {
      return loggerConfiguration.Sink(new EventLogSink(fmtProvider));
    }
  }

  public record LogMessage(DateTimeOffset Timestamp, LogEventLevel Level, string Message, string? Exception = null);

  public class EventLogSink : ILogEventSink
  {
    public delegate void NewLogHandler(object? sender, LogMessage message);

    public static EventLogSink? Instance { get; private set; }
    private event NewLogHandler? NewLogEvent;

    private readonly IFormatProvider? _formatProvider;
    private readonly Queue<LogMessage> buffer = new();

    public EventLogSink(IFormatProvider? formatProvider)
    {
      _formatProvider = formatProvider;
      Instance = this;
    }

    public void AddHandler(NewLogHandler handler)
    {
      NewLogEvent += handler;
      DequeueBuffer();
    }
    public void RemoveHandler(NewLogHandler handler)
    {
      NewLogEvent -= handler;
    }

    public void Emit(LogEvent logEvent)
    {
      var message = new LogMessage(logEvent.Timestamp, logEvent.Level, logEvent.RenderMessage(_formatProvider), logEvent.Exception?.ToString());

      if (NewLogEvent == null)
        buffer.Enqueue(message);
      else
        NewLogEvent?.Invoke(this, message);
    }

    private void DequeueBuffer()
    {
      while (buffer.TryDequeue(out LogMessage? oldMessage) && oldMessage != null)
      {
        NewLogEvent?.Invoke(this, oldMessage);
      }
    }
  }
}

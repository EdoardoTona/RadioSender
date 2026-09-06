using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;

namespace RadioSender;

public static class EventLogSinkSinkExtensions
{
  public static LoggerConfiguration EventLogSink(
            this LoggerSinkConfiguration loggerConfiguration,
            IFormatProvider? fmtProvider = null)
  {
    return loggerConfiguration.Sink(new EventLogSink(fmtProvider));
  }
}

public record LogMessage(DateTimeOffset Timestamp, LogEventLevel Level, string Message, string? Exception = null,
  string? NodeId = null, string? EdgeId = null, string? SessionId = null);

public class EventLogSink : ILogEventSink
{
  public delegate void NewLogHandler(object? sender, LogMessage message);

  public static EventLogSink? Instance { get; private set; }
  private event NewLogHandler? NewLogEvent;

  private readonly IFormatProvider? _formatProvider;
  private readonly Queue<LogMessage> buffer = new();
  private readonly object _sync = new();

  public EventLogSink(IFormatProvider? formatProvider)
  {
    _formatProvider = formatProvider;
    Instance = this;
  }

  public void AddHandler(NewLogHandler handler)
  {
    LogMessage[] pending;
    lock (_sync)
    {
      NewLogEvent += handler;
      pending = buffer.ToArray();
    }
    foreach (var oldMessage in pending) handler(this, oldMessage);
  }
  public void RemoveHandler(NewLogHandler handler)
  {
    lock (_sync) NewLogEvent -= handler;
  }

  public void Emit(LogEvent logEvent)
  {
    string? Property(string name) => logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value?.ToString() : null;
    var message = new LogMessage(logEvent.Timestamp, logEvent.Level, logEvent.RenderMessage(_formatProvider), logEvent.Exception?.ToString(),
      Property("NodeId"), Property("EdgeId"), Property("SessionId"));
    NewLogHandler? handler;
    lock (_sync)
    {
      handler = NewLogEvent;
      if (buffer.Count >= 1000) buffer.Dequeue();
      buffer.Enqueue(message);
    }
    handler?.Invoke(this, message);
  }
}

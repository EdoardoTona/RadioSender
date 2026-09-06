using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Runtime;

public sealed record FlowLogEntry(long Id, LogMessage Log);
public sealed record FlowLogPage(long Cursor, bool HistoryExpired, IReadOnlyList<FlowLogEntry> Items);

public sealed class FlowLogs : IHostedService
{
  private readonly object _sync = new();
  private readonly Queue<FlowLogEntry> _history = new();
  private long _sequence;
  private EventLogSink? _sink;
  public Task StartAsync(CancellationToken cancellationToken)
  { _sink = EventLogSink.Instance; _sink?.AddHandler(Receive); return Task.CompletedTask; }
  public Task StopAsync(CancellationToken cancellationToken)
  { _sink?.RemoveHandler(Receive); return Task.CompletedTask; }
  private void Receive(object? sender, LogMessage message)
  {
    lock (_sync)
    { _history.Enqueue(new(++_sequence, message)); while (_history.Count > 5000) _history.Dequeue(); }
  }
  public FlowLogPage Read(string scope, string? nodeId, string? sessionId, long after)
  {
    lock (_sync)
    {
      var matches = _history.Where(e => e.Id > after && (scope == "all" ||
        (scope == "node" ? e.Log.NodeId == nodeId && (sessionId == null || e.Log.SessionId == sessionId) : e.Log.NodeId == null))).ToArray();
      return new(_sequence, after > 0 && (_history.TryPeek(out var first) && first.Id > after + 1 || matches.Length > 500), matches.TakeLast(500).ToArray());
    }
  }
}

using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace RadioSender.Flow.Modules;

public sealed record DeduplicateSettings
{
  [Range(1, 1000000), Display(Name = "Maximum remembered events", Description = "Oldest entries are forgotten when this limit is reached.")]
  public int Capacity { get; init; } = 100000;
  [Range(0, 604800), Display(Name = "Remember for (seconds)", Description = "0 keeps entries until the capacity limit or this node restarts.")]
  public int RetentionSeconds { get; init; }
}

public sealed class DeduplicateModule(DeduplicateSettings settings) : FlowModule
{
  private readonly object _sync = new();
  private readonly Dictionary<string, LinkedListNode<(string Key, bool Cancelled, long Time)>> _index = [];
  private readonly LinkedList<(string Key, bool Cancelled, long Time)> _order = [];
  public override Punch? Process(Punch punch, bool replay)
  {
    // Intentional replay neither consults nor changes live deduplication state.
    if (replay) return punch;
    lock (_sync)
    {
      while (_order.First is { } first && settings.RetentionSeconds > 0 && Stopwatch.GetElapsedTime(first.Value.Time).TotalSeconds >= settings.RetentionSeconds)
        Remove(first);
      var key = punch.UncancelledComparisonKey;
      if (_index.TryGetValue(key, out var existing))
      {
        if (existing.Value.Cancelled == punch.Cancellation) return null;
        Remove(existing);
      }
      _index[key] = _order.AddLast((key, punch.Cancellation, Stopwatch.GetTimestamp()));
      while (_order.Count > settings.Capacity) Remove(_order.First!);
      return punch;
    }
  }
  private void Remove(LinkedListNode<(string Key, bool Cancelled, long Time)> node)
  { _index.Remove(node.Value.Key); _order.Remove(node); }
}

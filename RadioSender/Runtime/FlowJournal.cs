using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Runtime;

public sealed record FlowObservation(long Id, Guid EventId, Guid ExecutionId, long? ReplayOf,
  string NodeId, string PortId, string Direction, string? EdgeId, long Revision,
  DateTimeOffset ObservedAt, Punch Punch, string Status, string? Detail);
public sealed record ObservationPage(long Cursor, bool HistoryExpired, long Evicted, IReadOnlyList<FlowObservation> Items);

public sealed class FlowJournal
{
  private readonly object _sync = new();
  private readonly LinkedList<FlowObservation> _order = new();
  private readonly Dictionary<long, LinkedListNode<FlowObservation>> _index = [];
  private readonly Dictionary<string, Queue<long>> _ports = [];
  private readonly Dictionary<string, long> _lastEvicted = [];
  private long _sequence;
  private long _evicted;
  private long _bytes;
  private static string Key(string node, string direction) => node + ":" + direction;
  private static long Estimate(FlowObservation observation) => 768L + 2L *
    (observation.Punch.CompetitorId.Length + observation.Punch.SourceId.Length + (observation.Detail?.Length ?? 0));

  public FlowObservation Add(Guid eventId, Guid executionId, long? replayOf, string nodeId, string direction,
    string? edgeId, long revision, Punch punch, string status = "Accepted", string? detail = null)
  {
    lock (_sync)
    {
      var item = new FlowObservation(++_sequence, eventId, executionId, replayOf, nodeId,
        direction == "output" ? "out" : "in", direction, edgeId, revision, DateTimeOffset.UtcNow, punch, status, detail);
      _index[item.Id] = _order.AddLast(item);
      _bytes += Estimate(item);
      var key = Key(nodeId, direction);
      if (!_ports.TryGetValue(key, out var port)) _ports[key] = port = new();
      port.Enqueue(item.Id);
      while (port.Count > 500) Remove(port.Dequeue());
      while (_order.Count > 10000 || _bytes > 16 * 1024 * 1024) Remove(_order.First!.Value.Id);
      return item;
    }
  }

  private void Remove(long id)
  {
    if (!_index.Remove(id, out var node)) return;
    _bytes -= Estimate(node.Value);
    _lastEvicted[Key(node.Value.NodeId, node.Value.Direction)] = id;
    _order.Remove(node);
    _evicted++;
  }

  public ObservationPage Read(string nodeId, string direction, long after = 0)
  {
    lock (_sync)
      return new(_sequence, after > 0 && _lastEvicted.GetValueOrDefault(Key(nodeId, direction)) > after, _evicted,
        _order.Where(o => o.NodeId == nodeId && o.Direction == direction && o.Id > after).ToArray());
  }

  public FlowObservation[] Resolve(string nodeId, string direction, IReadOnlyList<long> ids)
  {
    lock (_sync)
    {
      if (ids.Count is < 1 or > 250 || ids.Distinct().Count() != ids.Count)
        throw new Flow.FlowException("Select between 1 and 250 distinct observations.");
      return ids.Select(id => _index.TryGetValue(id, out var item) && item.Value.NodeId == nodeId && item.Value.Direction == direction
        ? item.Value : throw new Flow.FlowException("An observation expired or belongs to another node/port. Refresh the inspector.", 409)).ToArray();
    }
  }

  public void Clear()
  {
    lock (_sync)
    { _order.Clear(); _index.Clear(); _ports.Clear(); _lastEvicted.Clear(); _bytes = 0; _evicted = 0; }
  }
}

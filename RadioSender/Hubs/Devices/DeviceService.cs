using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace RadioSender.Hubs.Devices
{
  public class DeviceService : IDisposable
  {
    private readonly IHubContext<DeviceHub> _hubContext;

    private readonly SortedDictionary<string, Edge> _edges = new();
    private readonly SortedDictionary<string, Node> _nodes = new();

    private readonly Subject<object> _changes = new();
    private readonly IDisposable sub;

    public DeviceService(IHubContext<DeviceHub> hubContext)
    {
      _hubContext = hubContext;

      sub = _changes.Throttle(TimeSpan.FromMilliseconds(1000))
                        .Do(_ => Notify())
                        .Subscribe();
    }

    public void Dispose()
    {
      sub?.Dispose();
      _changes?.Dispose();
    }

    public void UpdateEdge(Edge edge)
    {
      _edges[edge.Id] = edge;

      if (!_nodes.ContainsKey(edge.From))
        _nodes[edge.From] = new Node(edge.From, edge.From, edge.Value, null, edge.LastSeen);

      if (!_nodes.ContainsKey(edge.To))
        _nodes[edge.To] = new Node(edge.To, edge.To, edge.Value, null, edge.LastSeen);

      _changes.OnNext(null);
    }
    public void UpdateNode(Node node)
    {
      _nodes[node.Id] = node;
      _changes.OnNext(null);
    }

    public void Notify()
    {
      _hubContext.Clients.All.SendAsync("graph", _edges.Values.ToList(), _nodes.Values.ToList());
    }
    public void Notify(string connectionId)
    {
      _hubContext.Clients.Client(connectionId).SendAsync("graph", _edges.Values.ToList(), _nodes.Values.ToList());
    }
  }
}

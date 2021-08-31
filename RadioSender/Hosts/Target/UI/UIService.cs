using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hubs;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.UI
{
  public sealed class UIService : ITarget, IDisposable
  {
    private IFilter _filter = Filter.Invariant;
    private UIConfiguration _configuration;
    private readonly IHubContext<DeviceHub, IDeviceHub> _hubContext;

    private readonly SortedDictionary<string, VisEdge> _hops = new();
    private readonly SortedDictionary<string, VisNode> _nodes = new();


    private readonly List<Punch> _punches = new();

    private readonly Subject<object?> _changes = new();
    private readonly IDisposable sub;

    public UIService(
      IEnumerable<IFilter> filters,
      IHubContext<DeviceHub, IDeviceHub> hubContext,
      HubEvents hubEvents,
      UIConfiguration configuration)
    {
      _configuration = configuration;
      _hubContext = hubContext;
      CustomLogSink.HubContext = hubContext;
      CustomLogSink.InitEvents(hubEvents);

      hubEvents.GroupJoined += HubEvents_GroupJoined;
      sub = _changes.Throttle(TimeSpan.FromMilliseconds(1000))
                        .Do(_ => Notify())
                        .Subscribe();


      _nodes[NodeNew.Localhost.Id] = VisNode.FromNode(NodeNew.Localhost);

      UpdateConfiguration(filters, configuration);
    }

    private async void HubEvents_GroupJoined(HubCallerContext context, string group)
    {
      try
      {
        if (_hubContext == null) return;

        if (group == DeviceHub.GROUP_GRAPH)
        {

          await _hubContext.Clients.Client(context.ConnectionId).Graph(_hops.Values.ToList(), _nodes.Values.ToList());

          await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, DeviceHub.GROUP_GRAPH);
        }
        else if (group == DeviceHub.GROUP_PUNCHES)
        {
          await _hubContext.Clients.Client(context.ConnectionId).Punches(_punches);

          await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, DeviceHub.GROUP_PUNCHES);
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception e)
      {
        Log.Error(e, "Exception Group Join");
      }
    }

    public void Dispose()
    {
      sub?.Dispose();
      _changes?.Dispose();
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration!, configuration as UIConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));
    }

    public async Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {

      if (dispatch.Nodes != null)
      {
        foreach (var n in dispatch.Nodes)
        {
          _nodes[n.Id] = VisNode.FromNode(n);

          _changes.OnNext(null);
          //await _hubContext.Clients.All.SendAsync("Node", n, ct);
        }
      }

      if (dispatch.Hops != null)
      {
        foreach (var h in dispatch.Hops)
        {
          _hops[h.Id] = VisEdge.FromHop(h);
          _changes.OnNext(null);
          //await _hubContext.Clients.All.SendAsync("Hop", h, ct);
        }
      }

      if (_hubContext.Clients == null)
        return;

      if (dispatch.Punches == null)
        return;

      var punches = _filter.Transform(dispatch.Punches);

      foreach (var punch in punches)
      {
        await _hubContext.Clients.Group(DeviceHub.GROUP_PUNCHES).Punch(punch);
        _punches.Add(punch);
      }
    }

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }


    public void Notify()
    {
      _hubContext.Clients.Group(DeviceHub.GROUP_GRAPH).Graph(_hops.Values.ToList(), _nodes.Values.ToList());
    }

  }
}

using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.UI
{
  public class UIService : Hub, ITarget
  {
    private IFilter _filter = Filter.Invariant;
    private UIConfiguration _configuration;

    public UIService(
      IEnumerable<IFilter> filters,
      UIConfiguration configuration)
    {
      _configuration = configuration;
      UpdateConfiguration(filters, configuration);
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
          Log.Information("Received node info: {@node}", n);
      }

      if (dispatch.Hops != null)
      {
        foreach (var h in dispatch.Hops)
          Log.Information("Received hop info: {@hop}", h);
      }

      if (Clients == null)
        return;

      if (dispatch.Punches == null)
        return;

      var punches = _filter.Transform(dispatch.Punches);

      foreach (var punch in punches)
      {
        await Clients.All.SendAsync("Punch", punch, ct);
      }
    }

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);

    }

  }
}

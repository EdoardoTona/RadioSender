using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
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

    public async Task SendPunch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (Clients == null)
        return;

      var punch = _filter.Transform(dispatch.Punches);

      if (punch == null)
        return;

      await Clients.All.SendAsync("Punch", punch, ct);
    }

    public async Task SendPunches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendPunch(dispatch, ct);

    }

  }
}

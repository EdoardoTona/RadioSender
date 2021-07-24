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
    private readonly IFilter _filter;
    private readonly UIConfiguration _configuration;
    public UIService(
      IEnumerable<IFilter> filters,
      UIConfiguration uiConfiguration)
    {
      _configuration = uiConfiguration;
      _filter = filters.GetFilter(_configuration.Filter);
    }
    public async Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (Clients == null)
        return;

      punch = _filter.Transform(punch);

      if (punch == null)
        return;

      await Clients.All.SendAsync("Punch", punch, ct);
    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);

    }

  }
}

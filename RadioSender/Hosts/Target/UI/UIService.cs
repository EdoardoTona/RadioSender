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
    private IFilter _filter;
    private UIConfiguration _configuration;

    public UIService(
      IEnumerable<IFilter> filters,
      UIConfiguration configuration)
    {
      UpdateConfiguration(filters, configuration);
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration, configuration as UIConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));
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

using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.UI
{
  public class UIService : Hub, ITarget
  {
    public UIService()
    {
    }
    public async Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (Clients == null)
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

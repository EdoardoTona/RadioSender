using Hangfire;
using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Target;
using RadioSender.Hosts.Target.Oribos;
using RadioSender.Hubs;
using Serilog;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common
{
  public class DispatcherService
  {
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IHubContext<PunchHub> _hubContext;
    private readonly IEnumerable<ITarget> _targets;
    public DispatcherService(
      IBackgroundJobClient backgroundJobClient,
      IHubContext<PunchHub> hubContext,
      IEnumerable<ITarget> targets)
    {
      _backgroundJobClient = backgroundJobClient;
      _hubContext = hubContext;
      _targets = targets;
    }

    public void PushPunch(Punch punch)
    {
      if (punch == null)
        return;

      Log.Information("Received punch " + punch);
      // TODO handle duplicate punches
      _hubContext.Clients.All.SendAsync("Punch", punch).Wait(); // TODO treats as target
      foreach (var target in _targets)
      {
        _backgroundJobClient.Enqueue(() => target.SendPunch(punch, default));
      }
    }

    public void PushPunches(IEnumerable<Punch> punches)
    {
      foreach (var p in punches)
        PushPunch(p);
    }

  }
}

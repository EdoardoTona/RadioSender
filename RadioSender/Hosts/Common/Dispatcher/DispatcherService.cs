using Hangfire;
using Microsoft.AspNetCore.SignalR;
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
    private readonly OribosService _oribosService;
    private readonly IHubContext<PunchHub> _hubContext;
    public DispatcherService(
      IBackgroundJobClient backgroundJobClient,
      IHubContext<PunchHub> hubContext,
      OribosService oribosService)
    {
      _backgroundJobClient = backgroundJobClient;
      _hubContext = hubContext;
      _oribosService = oribosService;
    }

    private readonly ConcurrentQueue<Punch> queue = new();

    public void PushPunch(Punch punch)
    {
      if (punch == null)
        return;

      Log.Information("Received punch " + punch);

      _hubContext.Clients.All.SendAsync("Punch", punch).Wait();
      _backgroundJobClient.Enqueue(() => _oribosService.SendPunch(punch, default));
    }

    public void PushPunches(IEnumerable<Punch> punches)
    {
      foreach (var p in punches)
        PushPunch(p);
    }

  }
}

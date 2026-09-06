using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using RadioSender.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hubs;

public sealed class FlowHub : Hub
{
  // Clients fetch cursor-based snapshots on invalidation, so slow clients cannot build an event backlog.
}

public sealed class FlowNotifications(IHubContext<FlowHub> hub, FlowRuntime runtime) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
    while (await timer.WaitForNextTickAsync(stoppingToken))
      await hub.Clients.All.SendAsync("Changed", runtime.Snapshot(), stoppingToken);
  }
}

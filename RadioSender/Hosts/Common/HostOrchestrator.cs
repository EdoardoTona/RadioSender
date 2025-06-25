using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common;

public class HostOrchestrator(IServiceProvider serviceProvider) : IHostedService
{
  private readonly IEnumerable<IRadioSenderHost> services = serviceProvider.GetServices<IRadioSenderHost>();
  public Task StartAsync(CancellationToken cancellationToken)
  {
    var tasks = services.Select(s => s.StartAsync(cancellationToken));

    return Task.WhenAll(tasks);
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    var tasks = services.Select(s => s.StopAsync(cancellationToken));

    return Task.WhenAll(tasks);
  }
}

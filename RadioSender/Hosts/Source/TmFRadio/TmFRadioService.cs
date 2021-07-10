using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hubs;
using RadioSender.Hubs.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public class TmFRadioService : IHostedService
  {
    private readonly IReadOnlyList<TmFRadioGateway> _gateways;

    public TmFRadioService(DispatcherService dispatcherService, DeviceService deviceService, IEnumerable<Gateway> gateways)
    {
      _gateways = gateways.Select(g => new TmFRadioGateway(dispatcherService, deviceService, g)).ToList();
    }

    public Task StartAsync(CancellationToken st)
    {
      return Task.WhenAll(_gateways.Select(p => p.Start(st)));
    }

    public Task StopAsync(CancellationToken st)
    {
      return Task.WhenAll(_gateways.Select(p => p.Stop(st)));
    }
  }
}

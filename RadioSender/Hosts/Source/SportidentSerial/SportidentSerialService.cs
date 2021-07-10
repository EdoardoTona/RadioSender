using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SportidentSerial
{
  public class SportidentSerialService : IHostedService
  {
    private readonly IReadOnlyList<SportidentSerialPort> _ports;

    public SportidentSerialService(DispatcherService dispatcherService, IEnumerable<Port> ports)
    {
      _ports = ports.Select(p => new SportidentSerialPort(dispatcherService, p)).ToList();
    }

    public Task StartAsync(CancellationToken st)
    {
      return Task.WhenAll(_ports.Select(p => p.Start(st)));
    }

    public Task StopAsync(CancellationToken st)
    {
      return Task.WhenAll(_ports.Select(p => p.Stop(st)));
    }

  }
}

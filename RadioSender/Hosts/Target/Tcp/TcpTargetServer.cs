using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public class TcpTargetServer : IHostedService, ITarget
  {
    private IFilter _filter = Filter.Invariant;
    private TcpTargetConfiguration _configuration;

    private TcpServer _tcpServer;



    public TcpTargetServer(
      IEnumerable<IFilter> filters,
      TcpTargetConfiguration configuration)
    {
      UpdateConfiguration(filters, configuration);
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration, configuration as TcpTargetConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));

      var newServer = new TcpServer(_configuration.Address, _configuration.Port);
      newServer.Start();

      var oldServer = Interlocked.Exchange(ref _tcpServer, newServer);

      if (oldServer != null)
      {
        oldServer.Stop();
        oldServer.Dispose();
      }

    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (_tcpServer.ConnectedSessions == 0)
        return Task.CompletedTask;

      punch = _filter.Transform(punch);

      if (punch == null)
        return Task.CompletedTask;

      byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      foreach (var session in _tcpServer.GetSessions())
        session.Value.SendAsync(buffer);

      return Task.CompletedTask;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
      _tcpServer.Start();
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _tcpServer.Stop();
      return Task.CompletedTask;
    }


  }


}

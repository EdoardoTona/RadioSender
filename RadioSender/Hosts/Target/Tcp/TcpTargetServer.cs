using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public class TcpTargetServer : IHostedService, ITarget
  {
    private IFilter _filter = Filter.Invariant;
    private TcpTargetConfiguration _configuration;

    private TcpServer? _tcpServer;

    public TcpTargetServer(
      IEnumerable<IFilter> filters,
      TcpTargetConfiguration configuration)
    {
      _configuration = configuration;
      UpdateConfiguration(filters, configuration);
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration!, configuration as TcpTargetConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));

      if (_configuration.Address == null || _configuration.Port == null)
        return;

      var newServer = new TcpServer(_configuration.Port.Value);
      newServer.Start();

      var oldServer = Interlocked.Exchange(ref _tcpServer, newServer);

      if (oldServer != null)
      {
        oldServer.Stop();
        oldServer.Dispose();
      }

    }

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (_tcpServer == null || _tcpServer.ConnectedSessions == 0 || string.IsNullOrWhiteSpace(_configuration.Format))
        return Task.CompletedTask;

      var punches = _filter.Transform(dispatch.Punches);

      if (!punches.Any())
        return Task.CompletedTask;

      foreach (var punch in punches)
      {
        byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

        if (buffer == null || buffer.Length == 0)
          continue;

        foreach (var session in _tcpServer.GetSessions())
          session.Value.SendAsync(buffer);
      }

      return Task.CompletedTask;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
      _tcpServer?.Start();
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _tcpServer?.Stop();
      return Task.CompletedTask;
    }


  }


}

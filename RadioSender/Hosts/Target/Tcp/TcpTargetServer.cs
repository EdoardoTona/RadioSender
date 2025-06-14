using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public sealed class TcpTargetServer(
     FilterService filterService,
    TcpTargetConfiguration configuration) : IHostedService, ITarget, IDisposable
  {
    private readonly TcpServer? _tcpServer;

    public TcpTargetConfiguration GetConfiguration() => configuration;

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null || _tcpServer == null || _tcpServer.ConnectedSessions == 0 || string.IsNullOrWhiteSpace(configuration.Format))
        return Task.CompletedTask;

      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);

      foreach (var punch in punches)
      {
        byte[] buffer = FormatStringHelper.GetBytes(punch, configuration.Format);

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

    public void Dispose()
    {
      _tcpServer?.Dispose();
    }
  }


}

using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public sealed class TcpTargetServer : IRadioSenderHost, ITarget, IDisposable
  {
    private readonly FilterService _filterService;
    private readonly TcpTargetConfiguration _configuration;
    private readonly TcpServer? _tcpServer;
    public TcpTargetServer(
     FilterService filterService,
    TcpTargetConfiguration configuration)
    {
      _filterService = filterService;
      _configuration = configuration;

      if (_configuration.Port == null)
      {
        Log.Warning("Invalid TcpTargetServer configuration");
        return;
      }

      _tcpServer = new TcpServer(_configuration.Port.Value);
      _tcpServer.Start();

    }


    public TcpTargetConfiguration GetConfiguration() => _configuration;

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null || _tcpServer == null || _tcpServer.ConnectedSessions == 0 || string.IsNullOrWhiteSpace(_configuration.Format))
        return Task.CompletedTask;

      var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

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

    public void Dispose()
    {
      _tcpServer?.Dispose();
    }
  }


}

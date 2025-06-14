using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public sealed class TcpTargetClient(
    FilterService filterService,
    TcpTargetConfiguration configuration) : ITarget, IDisposable
  {
    private readonly TcpClient? _tcpClient;

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatcher, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatcher)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null || _tcpClient == null || !_tcpClient.IsConnected || string.IsNullOrWhiteSpace(configuration.Format))
        return Task.CompletedTask;

      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);

      foreach (var punch in punches)
      {

        byte[] buffer = FormatStringHelper.GetBytes(punch, configuration.Format);

        if (buffer == null || buffer.Length == 0)
          continue;

        _tcpClient.SendAsync(buffer);
      }

      return Task.CompletedTask;
    }

    public void Dispose()
    {
      _tcpClient?.DisconnectAndStop();
      _tcpClient?.Dispose();
    }
  }

}

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
  public sealed class TcpTargetClient : ITarget, IDisposable
  {
    private readonly FilterService _filterService;
    private readonly TcpTargetConfiguration _configuration;
    private readonly TcpClient? _tcpClient;

    public TcpTargetClient(
    FilterService filterService,
    TcpTargetConfiguration configuration)
    {
      _filterService = filterService;
      _configuration = configuration;

      if (_configuration.Address == null || _configuration.Port == null)
      {
        Log.Warning("Invalid TcpTargetClient configuration");
        return;
      }

      var address = _configuration.Address == "localhost" ? "127.0.0.1" : _configuration.Address;

      _tcpClient = new TcpClient(address, _configuration.Port.Value)
      {
        OptionKeepAlive = true
      };
      _tcpClient.ConnectAsync();

    }


    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatcher, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatcher)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null || _tcpClient == null || !_tcpClient.IsConnected || string.IsNullOrWhiteSpace(_configuration.Format))
        return Task.CompletedTask;

      var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

      foreach (var punch in punches)
      {

        byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

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

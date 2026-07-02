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
        var p = punch;
        if (p.CompetitorStatus == CompetitorStatus.Running || p.CompetitorStatus == CompetitorStatus.WaitingStart)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 0) };
        if (p.CompetitorStatus == CompetitorStatus.DNS)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 1) };
        if (p.CompetitorStatus == CompetitorStatus.DNF)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 2) };
        if (p.CompetitorStatus == CompetitorStatus.MP)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 3) };
        if (p.CompetitorStatus == CompetitorStatus.DSQ)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 4) };
        if (p.CompetitorStatus == CompetitorStatus.OverTime)
          p = p with { Time = new DateTime(1, 1, 1, 0, 0, 5) };

        byte[] buffer = FormatStringHelper.GetBytes(p, _configuration.Format);

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

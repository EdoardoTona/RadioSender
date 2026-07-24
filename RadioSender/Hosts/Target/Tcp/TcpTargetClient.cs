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

      var sendsCancellation = FormatStringHelper.UsesPlaceholder(_configuration.Format, "Cancellation");
      var sendsStatus = FormatStringHelper.UsesPlaceholder(_configuration.Format, "Status");
      // Without {Status}, the sentinel {Time} values below are the only way to convey status,
      // so it's only unrepresentable (and must be suppressed) when {Time} is absent too.
      var canRepresentStatus = sendsStatus || FormatStringHelper.UsesPlaceholder(_configuration.Format, "Time");

      foreach (var punch in punches)
      {
        // The receiver has no way to tell a cancellation/status change apart from a normal
        // punch unless the format carries it explicitly, so skip what it can't represent.
        if (punch.Cancellation && !sendsCancellation)
          continue;
        if (punch.CompetitorStatus != CompetitorStatus.Unknown && !canRepresentStatus)
          continue;

        var p = punch;
        // Only needed as a fallback when the format has no explicit {Status}; otherwise it
        // would clobber a real {Time} that's meant to be sent alongside {Status}.
        if (!sendsStatus)
        {
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
        }

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

using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public sealed class TcpTargetClient : ITarget, IDisposable
  {
    private IFilter _filter = Filter.Invariant;
    private TcpTargetConfiguration _configuration;

    private TcpClient? _tcpClient;

    public TcpTargetClient(
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

      var address = _configuration.Address == "localhost" ? "127.0.0.1" : _configuration.Address;

      var newClient = new TcpClient(address, _configuration.Port.Value);
      newClient.OptionKeepAlive = true;
      newClient.ConnectAsync();

      var oldClient = Interlocked.Exchange(ref _tcpClient, newClient);

      if (oldClient != null)
      {
        oldClient.DisconnectAndStop();
        oldClient.Dispose();
      }

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

      var punches = _filter.Transform(dispatch.Punches);

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

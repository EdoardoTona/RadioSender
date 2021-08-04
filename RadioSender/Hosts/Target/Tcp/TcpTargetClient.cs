using NetCoreServer;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public class TcpTargetClient : TcpClient, ITarget
  {
    private readonly IFilter _filter = Filter.Invariant;
    private readonly TcpTargetConfiguration _configuration;

    private bool _stop;

    public TcpTargetClient(
      IEnumerable<IFilter> filters,
      TcpTargetConfiguration configuration) : base(configuration.Address, configuration.Port)
    {
      _configuration = configuration;
      _filter = filters.GetFilter(_configuration.Filter);

      ConnectAsync();
    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (_stop || !IsConnected)
        return Task.CompletedTask;

      punch = _filter.Transform(punch);

      if (punch == null)
        return Task.CompletedTask;

      byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      SendAsync(buffer);

      return Task.CompletedTask;
    }

    public void DisconnectAndStop()
    {
      _stop = true;
      DisconnectAsync();
      while (IsConnected)
        Thread.Yield();
    }

    protected override void OnConnected()
    {
      Log.Information("TcpTargetClient {id} connected", Id);
    }

    protected override void OnDisconnected()
    {
      Log.Information("TcpTargetClient {id} disconnected", Id);

      // Wait for a while...
      Thread.Sleep(1000);

      // Try to connect again
      if (!_stop)
        ConnectAsync();
    }


  }

}

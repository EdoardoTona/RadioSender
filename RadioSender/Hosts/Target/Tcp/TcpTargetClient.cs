using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public class TcpTargetClient : ITarget
  {
    private IFilter _filter = Filter.Invariant;
    private TcpTargetConfiguration _configuration;

    private TcpClient _tcpClient;

    public TcpTargetClient(
      IEnumerable<IFilter> filters,
      TcpTargetConfiguration configuration)
    {
      UpdateConfiguration(filters, configuration);
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration, configuration as TcpTargetConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));

      var newClient = new TcpClient(_configuration.Address, _configuration.Port);
      newClient.ConnectAsync();

      var oldClient = Interlocked.Exchange(ref _tcpClient, newClient);

      if (oldClient != null)
      {
        oldClient.DisconnectAndStop();
        oldClient.Dispose();
      }

    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (!_tcpClient.IsConnected)
        return Task.CompletedTask;

      punch = _filter.Transform(punch);

      if (punch == null)
        return Task.CompletedTask;

      byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      _tcpClient.SendAsync(buffer);

      return Task.CompletedTask;
    }

  }

}

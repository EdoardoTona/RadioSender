using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Tcp
{
  public class TcpTargetServer : TcpServer, IHostedService, ITarget
  {
    private readonly IFilter _filter = Filter.Invariant;
    private readonly TcpTargetConfiguration _configuration;

    public TcpTargetServer(
      IEnumerable<IFilter> filters,
      TcpTargetConfiguration configuration) : base(IPAddress.Any, configuration.Port)
    {
      _configuration = configuration;
      _filter = filters.GetFilter(_configuration.Filter);
    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (ConnectedSessions == 0)
        return Task.CompletedTask;

      punch = _filter.Transform(punch);

      if (punch == null)
        return Task.CompletedTask;

      byte[] buffer = FormatStringHelper.GetBytes(punch, _configuration.Format);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      foreach (var session in Sessions)
        session.Value.SendAsync(buffer);

      return Task.CompletedTask;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
      Start();
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      Stop();
      return Task.CompletedTask;
    }

    protected override TcpSession CreateSession() { return new TcpTargetSession(this); }

    protected override void OnError(SocketError error)
    {
      Log.Warning("TcpTargetServer socket error {error}", error);
    }


  }


  class TcpTargetSession : TcpSession
  {
    public TcpTargetSession(TcpTargetServer server) : base(server) { }

    protected override void OnConnected()
    {
      Log.Information("TcpTargetServer client {id} connected", Id.ToString());
    }

    protected override void OnDisconnected()
    {
      Log.Information("TcpTargetServer client {id} disconnected", Id.ToString());
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      // ignore
    }

    protected override void OnError(SocketError error)
    {
      Log.Warning("TcpTargetServer client {id} socket error {error}", Id.ToString(), error);
    }
  }

}

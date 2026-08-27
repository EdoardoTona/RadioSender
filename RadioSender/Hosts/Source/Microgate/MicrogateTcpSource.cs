using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public sealed class MicrogateTcpSource : MicrogateSource
{
  private readonly Client _client;

  public MicrogateTcpSource(
    FilterService filterService,
    DispatcherService dispatcherService,
    MicrogateSourceConfiguration configuration)
    : base(filterService, dispatcherService, configuration, GetEndpoint(configuration))
  {
    _client = new Client(this, configuration.Address!, configuration.Port!.Value);
  }

  public override Task StartAsync(CancellationToken cancellationToken)
  {
    Log.Information("MicrogateSource connecting to {endpoint}", Endpoint);
    _client.ConnectAsync();
    return Task.CompletedTask;
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    if (!BeginStop())
      return;

    _client.DisconnectAsync();
    while (_client.IsConnected)
      await Task.Yield();
  }

  protected override void SendCore(ReadOnlySpan<byte> data)
  {
    if (!_client.SendAsync(data.ToArray()))
      throw new IOException($"TCP source {Endpoint} is not connected.");
  }

  protected override void DisposeTransport()
  {
    _client.Dispose();
  }

  private void Connected()
  {
    Log.Information("MicrogateSource {endpoint} TCP connected", Endpoint);
    OnTransportConnected();
  }

  private void Disconnected()
  {
    if (IsStopping)
      return;

    Log.Warning("MicrogateSource {endpoint} disconnected or unavailable; retrying", Endpoint);
    _ = ReconnectAsync();
  }

  private async Task ReconnectAsync()
  {
    try
    {
      await Task.Delay(1000, LifetimeToken).ConfigureAwait(false);
      if (!IsStopping)
        _client.ConnectAsync();
    }
    catch (OperationCanceledException)
    {
      // Normal shutdown.
    }
    catch (Exception e)
    {
      Log.Error("MicrogateSource reconnect error: {error}", e.Message);
    }
  }

  private static string GetEndpoint(MicrogateSourceConfiguration configuration)
  {
    if (!string.IsNullOrWhiteSpace(configuration.PortName))
      throw new ArgumentException("A TCP Microgate source cannot define PortName.", nameof(configuration));

    if (string.IsNullOrWhiteSpace(configuration.Address) || !configuration.Port.HasValue)
      throw new ArgumentException("A TCP Microgate source requires Address and Port.", nameof(configuration));

    return $"{configuration.Address}:{configuration.Port}";
  }

  private sealed class Client : NetCoreServer.TcpClient
  {
    private readonly MicrogateTcpSource _source;

    public Client(MicrogateTcpSource source, string address, int port)
      : base(address, port)
    {
      _source = source;
      OptionKeepAlive = true;
      OptionTcpKeepAliveInterval = 15;
      OptionTcpKeepAliveRetryCount = 3;
      OptionTcpKeepAliveTime = 5;
      OptionNoDelay = true;
    }

    protected override void OnConnected() => _source.Connected();
    protected override void OnDisconnected() => _source.Disconnected();
    protected override void OnReceived(byte[] buffer, long offset, long size) => _source.OnReceived(buffer, offset, size);
    protected override void OnError(SocketError error) => Log.Warning("MicrogateSource socket error {error}", error);
  }
}

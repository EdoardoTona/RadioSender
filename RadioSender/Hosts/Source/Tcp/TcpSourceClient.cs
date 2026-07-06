using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Tcp
{
  /// <summary>
  /// Connects out to a remote Address:Port and reads punches from the incoming
  /// text stream. Reconnects automatically if the connection drops.
  /// </summary>
  public sealed class TcpSourceClient : ISource, IRadioSenderHost, IDisposable
  {
    private readonly TcpSourceConfiguration _configuration;
    private readonly TcpSourceLineReader _reader;
    private InnerClient? _client;

    public TcpSourceClient(
      FilterService filterService,
      DispatcherService dispatcherService,
      TcpSourceConfiguration configuration)
    {
      _configuration = configuration;

      var parser = FormatStringParser.TryCreate(configuration.Format ?? "");
      if (parser == null)
        Log.Warning("Tcp source client has no parseable Format, no punches will be read");

      _reader = new TcpSourceLineReader(filterService, dispatcherService, configuration, parser);

      if (string.IsNullOrWhiteSpace(configuration.Address) || configuration.Port == null)
        Log.Warning("Invalid TcpSourceClient configuration (Address/Port required)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(_configuration.Address) || _configuration.Port == null)
        return Task.CompletedTask;

      var address = _configuration.Address == "localhost" ? "127.0.0.1" : _configuration.Address!;
      _client = new InnerClient(address, _configuration.Port.Value, _reader)
      {
        OptionKeepAlive = true
      };
      _client.ConnectAsync();
      Log.Information("Tcp source client connecting to {address}:{port}", address, _configuration.Port);
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _client?.DisconnectAndStop();
      return Task.CompletedTask;
    }

    public void Dispose()
    {
      _client?.DisconnectAndStop();
      _client?.Dispose();
    }

    private sealed class InnerClient(string address, int port, TcpSourceLineReader reader)
      : NetCoreServer.TcpClient(address, port)
    {
      private bool _stop;
      private string RemoteEndpoint => $"{Address}:{Port}";

      public void DisconnectAndStop()
      {
        _stop = true;
        DisconnectAsync();
        while (IsConnected)
          Thread.Yield();
      }

      protected override void OnConnected()
      {
        reader.Reset();
        Log.Information("Tcp source client {endpoint} connected", RemoteEndpoint);
      }

      protected override void OnReceived(byte[] buffer, long offset, long size)
      {
        if (size == 0)
          return;
        reader.Feed(new ReadOnlySpan<byte>(buffer, (int)offset, (int)size), RemoteEndpoint);
      }

      protected override void OnDisconnected()
      {
        Log.Information("Tcp source client {endpoint} disconnected", RemoteEndpoint);
        Thread.Sleep(1000);
        if (!_stop)
          ConnectAsync();
      }

      protected override void OnError(SocketError error)
      {
        Log.Warning("Tcp source client {endpoint} socket error {error}", RemoteEndpoint, error);
      }
    }
  }
}

using NetCoreServer;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Tcp
{
  /// <summary>
  /// Listens on Port and reads punches from every connected client's text stream.
  /// Each session keeps its own partial-line buffer.
  /// </summary>
  public sealed class TcpSourceServer : ISource, IRadioSenderHost, IDisposable
  {
    private readonly FilterService _filterService;
    private readonly DispatcherService _dispatcherService;
    private readonly TcpSourceConfiguration _configuration;
    private readonly FormatStringParser? _parser;
    private InnerServer? _server;

    public TcpSourceServer(
      FilterService filterService,
      DispatcherService dispatcherService,
      TcpSourceConfiguration configuration)
    {
      _filterService = filterService;
      _dispatcherService = dispatcherService;
      _configuration = configuration;

      _parser = FormatStringParser.TryCreate(configuration.Format ?? "");
      if (_parser == null)
        Log.Warning("Tcp source server has no parseable Format, no punches will be read");

      if (configuration.Port == null)
        Log.Warning("Invalid TcpSourceServer configuration (Port required)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      if (_configuration.Port == null)
        return Task.CompletedTask;

      _server = new InnerServer(_configuration.Port.Value, CreateReader);
      _server.Start();
      Log.Information("Tcp source server listening on port {port}", _configuration.Port);
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _server?.Stop();
      return Task.CompletedTask;
    }

    public void Dispose()
    {
      _server?.Dispose();
    }

    private TcpSourceLineReader CreateReader() =>
      new(_filterService, _dispatcherService, _configuration, _parser);

    private sealed class InnerServer(int port, Func<TcpSourceLineReader> readerFactory)
      : TcpServer(IPAddress.Any, port)
    {
      public Func<TcpSourceLineReader> ReaderFactory => readerFactory;

      protected override TcpSession CreateSession() => new InnerSession(this);

      protected override void OnError(SocketError error)
      {
        Log.Warning("Tcp source server socket error {error}", error);
      }
    }

    private sealed class InnerSession : TcpSession
    {
      private readonly TcpSourceLineReader _reader;
      private string? _endpoint;

      public InnerSession(InnerServer server) : base(server)
      {
        _reader = server.ReaderFactory();
      }

      protected override void OnConnected()
      {
        try { _endpoint = Socket.RemoteEndPoint?.ToString(); } catch { }
        Log.Information("Tcp source server client {endpoint} connected", _endpoint);
      }

      protected override void OnReceived(byte[] buffer, long offset, long size)
      {
        if (size == 0)
          return;
        _reader.Feed(new ReadOnlySpan<byte>(buffer, (int)offset, (int)size), _endpoint ?? Id.ToString());
      }

      protected override void OnDisconnected()
      {
        Log.Information("Tcp source server client {endpoint} disconnected", _endpoint);
      }

      protected override void OnError(SocketError error)
      {
        Log.Warning("Tcp source server client {endpoint} socket error {error}", _endpoint, error);
      }
    }
  }
}

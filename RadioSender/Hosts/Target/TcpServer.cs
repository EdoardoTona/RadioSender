using Serilog;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RadioSender.Hosts.Target
{
  public class TcpServer : NetCoreServer.TcpServer
  {
    public TcpServer(string address, int port) : base(IPAddress.Any, port) { }

    public ConcurrentDictionary<Guid, NetCoreServer.TcpSession> GetSessions() { return Sessions; }

    protected override NetCoreServer.TcpSession CreateSession() { return new TcpServerTcpSession(this); }

    protected override void OnError(SocketError error)
    {
      Log.Warning("TcpTargetServer socket error {error}", error);
    }

  }


  class TcpServerTcpSession : NetCoreServer.TcpSession
  {
    public TcpServerTcpSession(TcpServer server) : base(server) { }

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

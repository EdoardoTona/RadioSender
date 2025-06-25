using System.Threading;

namespace RadioSender.Hosts.Target
{

  public class TcpClient(string address, int port) : NetCoreServer.TcpClient(address, port)
  {
    private bool _stop;

    public void DisconnectAndStop()
    {
      _stop = true;
      DisconnectAsync();
      while (IsConnected)
        Thread.Yield();
    }

    protected override void OnConnected()
    {
      //Log.Information("TcpTargetClient {address}:{port} connected", Address, Port);
    }

    protected override void OnDisconnected()
    {
      //Log.Information("TcpTargetClient {address}:{port} disconnected", Address, Port);

      // Wait for a while...
      Thread.Sleep(1000);

      // Try to connect again
      if (!_stop)
        ConnectAsync();
    }

  }

}

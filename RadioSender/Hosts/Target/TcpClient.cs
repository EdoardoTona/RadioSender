using Serilog;
using System.Threading;

namespace RadioSender.Hosts.Target
{

  public class TcpClient : NetCoreServer.TcpClient
  {
    private bool _stop;

    public TcpClient(string address, int port) : base(address, port)
    {

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
      Log.Information("SirapClient {id} connected", Id.ToString());
    }

    protected override void OnDisconnected()
    {
      Log.Information("SirapClient {id} disconnected", Id.ToString());

      // Wait for a while...
      Thread.Sleep(1000);

      // Try to connect again
      if (!_stop)
        ConnectAsync();
    }

  }

}

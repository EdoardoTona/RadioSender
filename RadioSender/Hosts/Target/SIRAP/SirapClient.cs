using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.SIRAP
{
  public class SirapClient : TcpClient, ITarget
  {
    private readonly IFilter _filter = Filter.Invariant;
    private readonly SirapClientConfiguration _configuration;

    private bool _stop;

    public SirapClient(
      IEnumerable<IFilter> filters,
      SirapClientConfiguration configuration) : base(configuration.Address, configuration.Port)
    {
      _configuration = configuration;
      _filter = filters.GetFilter(_configuration.Filter);

      ConnectAsync();
    }

    public Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      if (_stop || !IsConnected)
        return Task.CompletedTask;

      punch = _filter.Transform(punch);

      if (punch == null)
        return Task.CompletedTask;

      byte[] buffer;
      if (_configuration.Version == 1)
      {
        buffer = OnReceivedV1(punch);
      }
      else
      {
        buffer = OnReceivedV2(punch);
      }

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      SendAsync(buffer);

      return Task.CompletedTask;
    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      foreach (var punch in punches)
        await SendPunch(punch, ct);
    }





    internal byte[] OnReceivedV1(Punch punch)
    {
      using var ms = new MemoryStream();
      using var bw = new BinaryWriter(ms);
      bw.Write((byte)0);

      bw.Write((ushort)punch.Control);

      if (!int.TryParse(punch.Card, out int chipNo))
        return null;

      bw.Write(chipNo);
      bw.Write((int)punch.Time.DayOfWeek); // Day information from SI punch (sunday 0 ... saturday 6)

      int time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 100 - (int)_configuration.ZeroTime.TotalMilliseconds / 100;
      if (time < 0) time += 10 * 3600 * 24;
      bw.Write(time);

      return ms.ToArray();
    }

    internal byte[] OnReceivedV2(Punch punch)
    {
      string name = "Radiosender";
      using var ms = new MemoryStream();
      using var bw = new BinaryWriter(ms);
      bw.Write((byte)name.Length);

      Span<byte> nameBuffer = new byte[20];
      Encoding.UTF8.GetBytes(name, nameBuffer);
      bw.Write(nameBuffer);
      bw.Write((byte)0);

      bw.Write((ushort)punch.Control);

      if (!int.TryParse(punch.Card, out int chipNo))
        return null;

      bw.Write(chipNo);

      bw.Write((int)punch.Time.DayOfWeek); // Day information from SI punch (sunday 0 ... saturday 6)

      int time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 10 - (int)_configuration.ZeroTime.TotalMilliseconds / 10;
      if (time < 0) time += 100 * 3600 * 24;
      bw.Write(time);

      return ms.ToArray();
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
      Log.Information("SirapClient {id} connected", Id);
    }

    protected override void OnDisconnected()
    {
      Log.Information("SirapClient {id} disconnected", Id);

      // Wait for a while...
      Thread.Sleep(1000);

      // Try to connect again
      if (!_stop)
        ConnectAsync();
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {

    }

    protected override void OnError(System.Net.Sockets.SocketError error)
    {
    }



  }

}

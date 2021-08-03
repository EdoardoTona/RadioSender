using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
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

      byte[] buffer = GetBytes(punch, _configuration.Version, _configuration.ZeroTime);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      SendAsync(buffer);

      return Task.CompletedTask;
    }

    private static byte[] GetBytes(Punch punch, int version, TimeSpan zeroTime)
    {
      if (!int.TryParse(punch.Card, out int chipNo))
        return null; // not numeric cards are not supported in SIRAP

      using var ms = new MemoryStream();
      using var bw = new BinaryWriter(ms);

      if (version == 2)
      {
        string name = "Radiosender";
        bw.Write((byte)name.Length);
        Span<byte> nameBuffer = new byte[20];
        Encoding.UTF8.GetBytes(name, nameBuffer);
        bw.Write(nameBuffer);
      }

      bw.Write((byte)0); // 0=punch, 255=Triggered time
      bw.Write((ushort)punch.Control);

      bw.Write(chipNo);
      bw.Write((int)punch.Time.DayOfWeek); // Day information from SI punch, sunday = 0

      int time;
      if (punch.Time == default)
      {
        time = 36000001; // invalid time
      }
      else if (version == 2)
      {
        // 1/100 resolution
        time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 10 - (int)zeroTime.TotalMilliseconds / 10;
        if (time < 0)
          time += 100 * 3600 * 24;
      }
      else
      {
        // 1/10 resolution
        time = (int)punch.Time.TimeOfDay.TotalMilliseconds / 100 - (int)zeroTime.TotalMilliseconds / 100;
        if (time < 0)
          time += 10 * 3600 * 24;
      }

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


  }

}

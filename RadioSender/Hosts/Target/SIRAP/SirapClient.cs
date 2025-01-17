using Microsoft.IO;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.SIRAP
{
  public sealed class SirapClient : ITarget, IDisposable
  {
    private static readonly RecyclableMemoryStreamManager _memoryManager = new();

    private IFilter _filter = Filter.Invariant;
    private SirapClientConfiguration _configuration;

    private TcpClient? _tcpClient;

    public SirapClient(
      IEnumerable<IFilter> filters,
      SirapClientConfiguration configuration)
    {
      _configuration = configuration;
      UpdateConfiguration(filters, configuration);
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration!, configuration as SirapClientConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));

      if (_configuration.Address == null || _configuration.Port == null)
        return;

      var address = _configuration.Address == "localhost" ? "127.0.0.1" : _configuration.Address;

      var newClient = new TcpClient(address, _configuration.Port.Value);
      newClient.ConnectAsync();

      var oldClient = Interlocked.Exchange(ref _tcpClient, newClient);

      if (oldClient != null)
      {
        oldClient.DisconnectAndStop();
        oldClient.Dispose();
      }

    }

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches == null || _tcpClient == null || !_tcpClient.IsConnected)
        return Task.CompletedTask;

      var punches = _filter.Transform(dispatch.Punches);

      foreach (var punch in punches)
      {
        var buffer = GetBytes(punch, _configuration.Version, _configuration.ZeroTime);

        if (buffer == null || buffer.Length == 0)
          continue;

        _tcpClient.SendAsync(buffer);
      }

      return Task.CompletedTask;
    }

    private static byte[]? GetBytes(Punch punch, int version, TimeSpan zeroTime)
    {
      if (!int.TryParse(punch.Card, out int chipNo))
        return null; // not numeric cards are not supported in SIRAP

      using var ms = _memoryManager.GetStream();
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

    public void Dispose()
    {
      _tcpClient?.Dispose();
    }
  }


}

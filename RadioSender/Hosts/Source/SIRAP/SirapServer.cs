using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SIRAP
{
  public sealed class SirapServer(
    FilterService filterService,
    DispatcherService dispatcherService,
    SirapServerConfiguration configuration)
    : TcpServer(IPAddress.Any, configuration.Port ?? throw new ArgumentNullException(nameof(configuration))), ISource, IRadioSenderHost, IDisposable
  {

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

    public new void Dispose()
    {
      DisconnectAll();
      base.Dispose();
    }

    protected override TcpSession CreateSession() { return new TcpSirapSession(this); }

    protected override void OnError(SocketError error)
    {
      Log.Warning("Sirap server socket error {error}", error);
    }

    internal void OnReceived(TcpSirapSession session, SirapFrame frame)
    {
      try
      {
        if (frame.Version == SirapProtocolVersion.V1)
        {
          OnReceivedV1(session, frame.Data);
        }
        else
        {
          OnReceivedV2(session, frame.Data);
        }
      }
      catch (Exception e)
      {
        Log.Error(e, "Error Sirap OnReceived");
      }
    }

    internal void OnReceivedV1(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
      byte type = buffer[0]; // 0=punch, 255=Triggered time
      var codeNo = BitConverter.ToUInt16(buffer.Slice(1, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(3, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(7, 4)); // Day information from SI punch, sunday = 0
      var codeTime = BitConverter.ToInt32(buffer.Slice(11, 4));
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore

      var time = TimeSpan.FromMilliseconds(codeTime * 100);

      if (!ManageSpecialFlags(codeDay, codeTime, ref time, out CompetitorStatus competitorStatus, out bool isCancellation))
        return;

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      var punch = filterService.Transform(
                    configuration.Filter,
                     new Punch(
                      ReceivedAt: DateTimeOffset.UtcNow,
                     CompetitorId: chipNo.ToString(),
                     CompetitorIdType: CompetitorIdType.PunchingCard,
                     Control: codeNo,
                     ControlType: codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
                     Time: dt,
                     SourceId: "Sirap", // TODO
                     Cancellation: isCancellation,
                     CompetitorStatus: competitorStatus
                     )
                  );

      if (punch != null)
        dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
    }

    internal void OnReceivedV2(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
      if (session.Name == null)
      {
        byte nameLength = buffer[0];
        session.Name = Encoding.UTF8.GetString(buffer.Slice(1, nameLength));
      }

#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
      byte type = buffer[21]; // 0=punch, 255=Triggered time

      var codeNo = BitConverter.ToUInt16(buffer.Slice(22, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(24, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(28, 4)); // Day information from SI punch
      var codeTime = BitConverter.ToInt32(buffer.Slice(32, 4));
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore

      var time = TimeSpan.FromMilliseconds(codeTime * 10);

      if (!ManageSpecialFlags(codeDay, codeTime, ref time, out CompetitorStatus competitorStatus, out bool isCancellation))
        return;

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      var punch = filterService.Transform(
                    configuration.Filter,
                     new Punch(
                      ReceivedAt: DateTimeOffset.UtcNow,
                     CompetitorId: chipNo.ToString(),
                     CompetitorIdType: CompetitorIdType.PunchingCard,
                     Control: codeNo,
                     ControlType: codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
                     Time: dt,
                     SourceId: "Sirap", // TODO
                     Cancellation: isCancellation,
                     CompetitorStatus: competitorStatus
                     )
                  );

      if (punch != null)
        dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
    }

    internal static bool ManageSpecialFlags(int codeDay,
      int codeTime,
      ref TimeSpan time,
      out CompetitorStatus competitorStatus,
      out bool isCancellation)
    {
      isCancellation = false;
      competitorStatus = CompetitorStatus.Unknown;

      if (codeDay == 0xFF)
      {
        // codeDay = 0xFF special flag (agreed with Simon Harston) to inform the time has a special meaning:
        // 00:00:00:cancel previous event
        // 00:00:01:DNS
        // 00:00:02:DNF
        // 00:00:03:MP
        // 00:00:04:DSQ
        // 00:00:05:OverTime

        if (codeTime == 360000001) // documented as "no time" in Kramer original specification
        {
          isCancellation = true;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 1)
        {
          competitorStatus = CompetitorStatus.DNS;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 2)
        {
          competitorStatus = CompetitorStatus.DNF;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 3)
        {
          competitorStatus = CompetitorStatus.MP;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 4)
        {
          competitorStatus = CompetitorStatus.DSQ;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 5)
        {
          competitorStatus = CompetitorStatus.OverTime;
          time = DateTime.Now.TimeOfDay;
        }

      }
      else
      {
        if (codeTime == 360000001)
          return false; // ignore 
      }

      return true;
    }
  }


  internal enum SirapProtocolVersion
  {
    V1,
    V2
  }

  internal readonly record struct SirapFrame(SirapProtocolVersion Version, byte[] Data);

  internal static class SirapFrameReader
  {
    public const int V1RecordLength = 15;
    public const int V2RecordLength = 36;
    private const byte PunchRecordType = 0;
    private const byte TriggeredTimeRecordType = 255;
    private const int MaxV2NameLength = 20;

    public static bool TryTakeFrame(List<byte> buffer,
      ref SirapProtocolVersion? version,
      out SirapFrame frame,
      out int discardedBytes)
    {
      frame = default;
      discardedBytes = 0;

      while (buffer.Count > 0)
      {
        var frameInfo = GetFrameInfo(buffer, version);
        if (frameInfo == null)
        {
          var invalidLength = CountInvalidStartBytes(buffer, version);
          buffer.RemoveRange(0, invalidLength);
          discardedBytes += invalidLength;
          continue;
        }

        var (frameVersion, frameLength) = frameInfo.Value;
        if (buffer.Count < frameLength)
          return false;

        var data = new byte[frameLength];
        buffer.CopyTo(0, data, 0, frameLength);
        buffer.RemoveRange(0, frameLength);

        version ??= frameVersion;
        frame = new SirapFrame(frameVersion, data);
        return true;
      }

      return false;
    }

    private static (SirapProtocolVersion Version, int Length)? GetFrameInfo(List<byte> buffer, SirapProtocolVersion? version)
    {
      if (version == SirapProtocolVersion.V1)
        return IsV1RecordType(buffer[0]) ? (SirapProtocolVersion.V1, V1RecordLength) : null;

      if (version == SirapProtocolVersion.V2)
        return IsValidV2NameLength(buffer[0]) ? (SirapProtocolVersion.V2, V2RecordLength) : null;

      return GetInitialFrameInfo(buffer);
    }

    private static (SirapProtocolVersion Version, int Length)? GetInitialFrameInfo(List<byte> buffer)
    {
      var firstByte = buffer[0];

      if (firstByte > 0 && IsValidV2NameLength(firstByte))
        return (SirapProtocolVersion.V2, V2RecordLength);

      if (firstByte == 0 && LooksLikeCompleteEmptyNameV2Frame(buffer))
        return (SirapProtocolVersion.V2, V2RecordLength);

      if (IsV1RecordType(firstByte))
        return (SirapProtocolVersion.V1, V1RecordLength);

      return null;
    }

    private static bool LooksLikeCompleteEmptyNameV2Frame(List<byte> buffer)
    {
      if (buffer.Count < V2RecordLength)
        return false;

      for (var i = 1; i <= MaxV2NameLength; i++)
      {
        if (buffer[i] != 0)
          return false;
      }

      return IsV1RecordType(buffer[21]);
    }

    private static int CountInvalidStartBytes(List<byte> buffer, SirapProtocolVersion? version)
    {
      var count = 0;
      while (count < buffer.Count && !IsPotentialStartByte(buffer[count], version))
      {
        count++;
      }

      return Math.Max(count, 1);
    }

    private static bool IsPotentialStartByte(byte value, SirapProtocolVersion? version)
    {
      if (version == SirapProtocolVersion.V1)
        return IsV1RecordType(value);

      if (version == SirapProtocolVersion.V2)
        return IsValidV2NameLength(value);

      return IsV1RecordType(value) || IsValidV2NameLength(value);
    }

    private static bool IsV1RecordType(byte value)
    {
      return value == PunchRecordType || value == TriggeredTimeRecordType;
    }

    private static bool IsValidV2NameLength(byte value)
    {
      return value <= MaxV2NameLength;
    }
  }

  class TcpSirapSession : TcpSession
  {
    private const int MaxBufferSize = 8192;
    private const int BufferTimeoutSeconds = 5;

    private readonly List<byte> _receiveBuffer = [];
    private DateTime _lastBufferUpdate = DateTime.MinValue;
    private SirapProtocolVersion? _protocolVersion;
    private string? _name;
    public string? Name
    {
      get => _name;
      set
      {
        if (_name == null)
        {
          _name = value;
          Log.Information("Sirap client {id} is {name}", Id, Name);
        }
      }
    }
    public TcpSirapSession(SirapServer server) : base(server) { }

    string? remoteEndpoint;

    protected override void OnConnected()
    {
      try
      {
        remoteEndpoint = Socket.RemoteEndPoint?.ToString();
        Log.Information("Sirap client {endpoint} connected", remoteEndpoint);
      }
      catch { }
    }

    protected override void OnDisconnected()
    {
      try
      {
        if (string.IsNullOrEmpty(Name))
          Log.Information("Sirap client {endpoint} disconnected", remoteEndpoint);
        else
          Log.Information("Sirap client {endpoint} (Name: {name}) disconnected", remoteEndpoint, Name);
      }
      catch { }
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      if (size == 0)
        return;

      var ros = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
      var now = DateTime.UtcNow;

      if (_receiveBuffer.Count > 0 &&
          _lastBufferUpdate != DateTime.MinValue &&
          (now - _lastBufferUpdate).TotalSeconds > BufferTimeoutSeconds)
      {
        Log.Warning("Sirap client {endpoint} buffer content is older than {timeout} seconds, clearing buffer",
          remoteEndpoint,
          BufferTimeoutSeconds);
        ClearReceiveBuffer();
      }

      _receiveBuffer.AddRange(ros);
      _lastBufferUpdate = now;

      if (_receiveBuffer.Count > MaxBufferSize)
      {
        Log.Warning("Sirap client {endpoint} buffer exceeded maximum size of {maxSize} bytes, clearing buffer",
          remoteEndpoint,
          MaxBufferSize);
        ClearReceiveBuffer();
        return;
      }

      while (true)
      {
        var hasFrame = SirapFrameReader.TryTakeFrame(_receiveBuffer, ref _protocolVersion, out var frame, out var discardedBytes);
        if (discardedBytes > 0)
          Log.Warning("Sirap client {endpoint} discarded {count} invalid frame byte(s)", remoteEndpoint, discardedBytes);

        if (!hasFrame)
          break;

        ((SirapServer)Server).OnReceived(this, frame);
      }

      if (_receiveBuffer.Count == 0)
        _lastBufferUpdate = DateTime.MinValue;
    }

    protected override void OnError(SocketError error)
    {
      Log.Warning("Sirap client {endpoint} socket error {error}", remoteEndpoint, error);
    }

    private void ClearReceiveBuffer()
    {
      _receiveBuffer.Clear();
      _lastBufferUpdate = DateTime.MinValue;
    }
  }

}

using Microgate.Common.Protocol.Rei2;
using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public class MicrogateSource : TcpClient, ISource, IRadioSenderHost, IDisposable
{
  public readonly FilterService _filterService;
  public readonly DispatcherService _dispatcherService;
  public readonly MicrogateSourceConfiguration _configuration;

  private int? serialNumber;
  public MicrogateSource(
  FilterService filterService,
  DispatcherService dispatcherService,
  MicrogateSourceConfiguration configuration)
    : base(configuration.Address ?? throw new ArgumentNullException(nameof(configuration)),
      configuration.Port ?? throw new ArgumentNullException(nameof(configuration)))
  {
    _filterService = filterService;
    _dispatcherService = dispatcherService;
    _configuration = configuration;

    OptionKeepAlive = true;
    OptionTcpKeepAliveInterval = 15;
    OptionTcpKeepAliveRetryCount = 3;
    OptionTcpKeepAliveTime = 5;
    OptionNoDelay = true;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    base.ConnectAsync();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    DisconnectAndStop();
    return Task.CompletedTask;
  }

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
    try
    {
      _ = AskSerialNumber();
      _ = AskRetransmission();

    }
    catch { }
  }

  protected override void OnDisconnected()
  {
    try
    {
      Log.Information("MicrogateSource {address}:{port} disconnected", Address.ToString(), Port);

      // Wait for a while...
      Thread.Sleep(1000);

      // Try to connect again
      bool res;
      if (!_stop)
        res = ConnectAsync();
    }
    catch (Exception e)
    {
      Log.Error("MicrogateSource OnDisconnected error: {error}", e.Message);
    }
  }

  public async Task AskRetransmission()
  {
    await Task.Delay(1500); // Wait for the connection to stabilize

    SendAsync(new Rei2StaticRequest()
    {
      RequestingDevice = 'R',
      RequestId = 1,
      CompetitorNumber = 0,
      Info = InfoExtEnum.TimeOfDay,
      LogicalChannel = 251, // means all the channels
      Run = 1,
      Output = OutputStaticEnum.S
    }.Raw);

  }
  public async Task AskSerialNumber()
  {
    await Task.Delay(1000); // Wait for the connection to stabilize

    SendAsync(new Rei2StatusRequest()
    {
      StatusCode = 9999,
      RequestingDevice = 'R',
      RequestId = 1
    }.Raw);

    SendAsync(new Rei2StatusRequest()
    {
      StatusCode = 1000,
      RequestingDevice = 'R',
      RequestId = 2
    }.Raw);
  }

  protected override void OnError(System.Net.Sockets.SocketError error)
  {
    Log.Warning("MicrogateSource socket error {error}", error);
  }
  private readonly List<byte> _receiveBuffer = [];
  private const int MAX_BUFFER_SIZE = 8192;
  private const int BUFFER_TIMEOUT_SECONDS = 5;
  private DateTime _lastBufferUpdate = DateTime.MinValue;

  protected override void OnReceived(byte[] buffer, long offset, long size)
  {
    var sBuffer = buffer.AsSpan((int)offset, (int)size); try
    {
      if (size == 0) return;

      var now = DateTime.UtcNow;

      // Check if buffer content is older than 5 seconds
      if (_receiveBuffer.Count > 0 && _lastBufferUpdate != DateTime.MinValue &&
          (now - _lastBufferUpdate).TotalSeconds > BUFFER_TIMEOUT_SECONDS)
      {
        Log.Warning("MicrogateSource buffer content is older than {timeout} seconds, clearing buffer", BUFFER_TIMEOUT_SECONDS);
        _receiveBuffer.Clear();
        _lastBufferUpdate = DateTime.MinValue;
      }

      // Append new data to handle fragmented messages.
      _receiveBuffer.AddRange(sBuffer);
      _lastBufferUpdate = now;      // Check if buffer exceeds maximum size
      if (_receiveBuffer.Count > MAX_BUFFER_SIZE)
      {
        Log.Warning("MicrogateSource buffer exceeded maximum size of {maxSize} bytes, clearing buffer", MAX_BUFFER_SIZE);
        _receiveBuffer.Clear();
        _lastBufferUpdate = DateTime.MinValue;
        return;
      }

      // Get a zero-copy span to process the buffer.
      var processingSpan = CollectionsMarshal.AsSpan(_receiveBuffer);
      int processedLength = 0;

      while (true)
      {
        var searchSpan = processingSpan[processedLength..];
        int delimiterPos = searchSpan.IndexOfAny((byte)'\r', (byte)'\n');

        // No delimiter found, wait for more data.
        if (delimiterPos == -1) break;

        var messageSpan = searchSpan[..delimiterPos];
        if (!messageSpan.IsEmpty)
        {
          ProcessMessage(messageSpan);
        }

        // Consume all sequential delimiters (handles CR, LF, CRLF).
        int startOfNextMessage = processedLength + delimiterPos + 1;
        while (startOfNextMessage < processingSpan.Length &&
               (processingSpan[startOfNextMessage] == (byte)'\r' || processingSpan[startOfNextMessage] == (byte)'\n'))
        {
          startOfNextMessage++;
        }

        processedLength = startOfNextMessage;
        if (processedLength >= processingSpan.Length) break;
      }      // Clean up the processed part of the buffer.
      if (processedLength > 0)
      {
        if (processedLength == _receiveBuffer.Count)
        {
          _receiveBuffer.Clear();
          _lastBufferUpdate = DateTime.MinValue;
        }
        else
        {
          _receiveBuffer.RemoveRange(0, processedLength);
          // Buffer still has content, keep the timestamp
        }
      }
    }
    catch (Exception e)
    {
      Log.Warning("MicrogateSource OnReceived error {error} on {buffer}", e.Message, Convert.ToBase64String(sBuffer));
    }
  }

  private void ProcessMessage(Span<byte> b)
  {
    var type = Rei2Msg.GetRei2MsgType(b);
    switch (type)
    {
      case Rei2MsgTypes.Rei2ExtData:
      case Rei2MsgTypes.Rei2StaticData:
        ProcessData(type, b);
        break;
      case Rei2MsgTypes.Rei2StatusReply:
        ProcessStatusMessage(b);
        break;

      default:
        break;
    }
  }

  private void ProcessStatusMessage(Span<byte> b)
  {
    if (b.Length < Rei2StatusReply.LENGTH)
    {
      Log.Information("MicrogateSource {address}:{port} (simulator) connected", Address.ToString(), Port);
      return;
    }

    var data = new Rei2StatusReply(b);

    if (data.StatusCode == 9999)
    {
      var snRaw = data.DataRaw.Slice(5, 4);

      if (!int.TryParse(snRaw, out var sn))
        return;

      serialNumber = sn;

      Log.Information("MicrogateSource {address}:{port} (serial number {sn}) connected", Address.ToString(), Port, sn);
      return;

    }
    else if (data.StatusCode == 1000)
    {
      var precision = (char)data.DataRaw[0] switch
      {
        '0' => "1s",
        '1' => "0.1s",
        '2' => "0.01s",
        '3' => "0.001s",
        '4' => "0.0001s",
        _ => "unknown"
      };

      var rounding = (char)data.DataRaw[1];
      var cuttingoff = (char)data.DataRaw[2] == '1';
      Log.Information("MicrogateSource {sn}: precision {p}, rounding {r}, cutting off {c}",
        serialNumber?.ToString() ?? "simulator", precision, rounding, cuttingoff);

    }
  }


  private void ProcessData(Rei2MsgTypes type, Span<byte> b)
  {
    CompetitorStatus status;
    PunchControlType controlType;
    string competitorNumber;
    int logicalChannel;
    DateTime time;
    bool annulled = false;
    if (type == Rei2MsgTypes.Rei2ExtData)
    {
      var data = new Rei2ExtData(b);

      if (data.CompetitorNumber == null || data.Timestamp == null || data.IsNetTime)
        return;

      status = data.Info switch
      {
        InfoExtEnum.DSQ => CompetitorStatus.DSQ,
        InfoExtEnum.DNS => CompetitorStatus.DNS,
        InfoExtEnum.DNF => CompetitorStatus.DNF,
        _ => CompetitorStatus.Unknown
      };

      controlType = data.LogicalChannel switch
      {
        0 => PunchControlType.Start,
        byte.MaxValue => PunchControlType.Finish,
        ushort.MaxValue => PunchControlType.Finish,
        _ => PunchControlType.Control
      };
      competitorNumber = data.CompetitorNumber.Value.ToString();
      logicalChannel = data.LogicalChannel;
      time = data.Timestamp.Value;
      annulled = data.Info == InfoExtEnum.Annulled;
    }
    else if (type == Rei2MsgTypes.Rei2StaticData)
    {
      var data = new Rei2StaticData(b);

      if (data.CompetitorNumber == null || data.Timestamp == null)
        return;

      status = data.Info switch
      {
        InfoExtEnum.DSQ => CompetitorStatus.DSQ,
        InfoExtEnum.DNS => CompetitorStatus.DNS,
        InfoExtEnum.DNF => CompetitorStatus.DNF,
        _ => CompetitorStatus.Unknown
      };

      controlType = data.LogicalChannel switch
      {
        0 => PunchControlType.Start,
        byte.MaxValue => PunchControlType.Finish,
        ushort.MaxValue => PunchControlType.Finish,
        _ => PunchControlType.Control
      };
      competitorNumber = data.CompetitorNumber.Value.ToString();
      logicalChannel = data.LogicalChannel;
      time = data.Timestamp.Value;
      annulled = data.Info == InfoExtEnum.Annulled;

    }
    else
    {
      return;
    }

    var punch = _filterService.Transform(
                   _configuration.Filter,
                    new Punch(
                     ReceivedAt: DateTimeOffset.UtcNow,
                    CompetitorId: competitorNumber,
                    CompetitorIdType: CompetitorIdType.BibNumber,
                    Control: logicalChannel,
                    ControlType: controlType,
                    Time: time,
                    SourceId: "Microgate " + serialNumber,
                    Cancellation: annulled,
                    CompetitorStatus: status
                    )
                 );

    if (punch != null)
      _dispatcherService.PushDispatch(new PunchDispatch([punch]));
  }

}

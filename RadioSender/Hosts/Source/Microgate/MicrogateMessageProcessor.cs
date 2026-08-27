using Microgate.Common.Protocol.Rei2;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RadioSender.Hosts.Source.Microplus;

internal sealed class MicrogateMessageProcessor(
  FilterService filterService,
  DispatcherService dispatcherService,
  MicrogateSourceConfiguration configuration,
  string endpoint)
{
  private const int MAX_BUFFER_SIZE = 8192;
  private const int BUFFER_TIMEOUT_SECONDS = 5;

  private readonly List<byte> _receiveBuffer = [];
  private DateTime _lastBufferUpdate = DateTime.MinValue;
  private int? _serialNumber;

  public void Reset()
  {
    _serialNumber = null;
    _receiveBuffer.Clear();
    _lastBufferUpdate = DateTime.MinValue;
  }

  public void Receive(ReadOnlySpan<byte> data)
  {
    try
    {
      if (data.IsEmpty)
        return;

      var now = DateTime.UtcNow;

      if (_receiveBuffer.Count > 0 && _lastBufferUpdate != DateTime.MinValue &&
          (now - _lastBufferUpdate).TotalSeconds > BUFFER_TIMEOUT_SECONDS)
      {
        Log.Warning("MicrogateSource buffer content is older than {timeout} seconds, clearing buffer", BUFFER_TIMEOUT_SECONDS);
        _receiveBuffer.Clear();
        _lastBufferUpdate = DateTime.MinValue;
      }

      _receiveBuffer.AddRange(data);
      _lastBufferUpdate = now;

      if (_receiveBuffer.Count > MAX_BUFFER_SIZE)
      {
        Log.Warning("MicrogateSource buffer exceeded maximum size of {maxSize} bytes, clearing buffer", MAX_BUFFER_SIZE);
        _receiveBuffer.Clear();
        _lastBufferUpdate = DateTime.MinValue;
        return;
      }

      ProcessBufferedMessages();
    }
    catch (Exception e)
    {
      Log.Warning("MicrogateSource receive error {error} on {buffer}",
        e.Message, Convert.ToBase64String(data));
    }
  }

  private void ProcessBufferedMessages()
  {
    var processingSpan = CollectionsMarshal.AsSpan(_receiveBuffer);
    int processedLength = 0;

    while (true)
    {
      var searchSpan = processingSpan[processedLength..];
      int delimiterPos = searchSpan.IndexOfAny((byte)'\r', (byte)'\n');

      if (delimiterPos == -1)
        break;

      var messageSpan = searchSpan[..delimiterPos];
      if (!messageSpan.IsEmpty)
        ProcessMessage(messageSpan);

      int startOfNextMessage = processedLength + delimiterPos + 1;
      while (startOfNextMessage < processingSpan.Length &&
             (processingSpan[startOfNextMessage] == (byte)'\r' || processingSpan[startOfNextMessage] == (byte)'\n'))
      {
        startOfNextMessage++;
      }

      processedLength = startOfNextMessage;
      if (processedLength >= processingSpan.Length)
        break;
    }

    if (processedLength == 0)
      return;

    if (processedLength == _receiveBuffer.Count)
    {
      _receiveBuffer.Clear();
      _lastBufferUpdate = DateTime.MinValue;
    }
    else
    {
      _receiveBuffer.RemoveRange(0, processedLength);
    }
  }

  private void ProcessMessage(Span<byte> message)
  {
    var type = Rei2Msg.GetRei2MsgType(message);
    switch (type)
    {
      case Rei2MsgTypes.Rei2ExtData:
      case Rei2MsgTypes.Rei2StaticData:
        ProcessData(type, message);
        break;
      case Rei2MsgTypes.Rei2StatusReply:
        ProcessStatusMessage(message);
        break;
    }
  }

  private void ProcessStatusMessage(Span<byte> message)
  {
    if (message.Length < Rei2StatusReply.LENGTH)
    {
      Log.Information("MicrogateSource {endpoint} (simulator) connected", endpoint);
      return;
    }

    var data = new Rei2StatusReply(message);

    if (data.StatusCode == 9999)
    {
      var serialNumberRaw = data.DataRaw.Slice(5, 4);
      if (!int.TryParse(serialNumberRaw, out var serialNumber))
        return;

      _serialNumber = serialNumber;
      Log.Information("MicrogateSource {endpoint} (serial number {sn}) connected", endpoint, serialNumber);
      return;
    }

    if (data.StatusCode != 1000)
      return;

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
    var cuttingOff = (char)data.DataRaw[2] == '1';
    Log.Information("MicrogateSource {sn}: precision {p}, rounding {r}, cutting off {c}",
      _serialNumber?.ToString() ?? "simulator", precision, rounding, cuttingOff);
  }

  private void ProcessData(Rei2MsgTypes type, Span<byte> message)
  {
    CompetitorStatus status;
    PunchControlType controlType;
    string competitorNumber;
    int logicalChannel;
    DateTime time;
    bool annulled;

    if (type == Rei2MsgTypes.Rei2ExtData)
    {
      var data = new Rei2ExtData(message);
      if (data.CompetitorNumber == null || data.Timestamp == null || data.IsNetTime)
        return;

      status = GetCompetitorStatus(data.Info);
      controlType = GetControlType(data.LogicalChannel);
      competitorNumber = data.CompetitorNumber.Value.ToString();
      logicalChannel = data.LogicalChannel;
      time = data.Timestamp.Value;
      annulled = data.Info == InfoExtEnum.Annulled;
    }
    else if (type == Rei2MsgTypes.Rei2StaticData)
    {
      var data = new Rei2StaticData(message);
      if (data.CompetitorNumber == null || data.Timestamp == null)
        return;

      status = GetCompetitorStatus(data.Info);
      controlType = GetControlType(data.LogicalChannel);
      competitorNumber = data.CompetitorNumber.Value.ToString();
      logicalChannel = data.LogicalChannel;
      time = data.Timestamp.Value;
      annulled = data.Info == InfoExtEnum.Annulled;
    }
    else
    {
      return;
    }

    var punch = filterService.Transform(
      configuration.Filter,
      new Punch(
        ReceivedAt: DateTimeOffset.UtcNow,
        CompetitorId: competitorNumber,
        CompetitorIdType: CompetitorIdType.BibNumber,
        Control: logicalChannel,
        ControlType: controlType,
        Time: time,
        SourceId: "Microgate " + _serialNumber,
        Cancellation: annulled,
        CompetitorStatus: status));

    if (punch != null)
      dispatcherService.PushDispatch(new PunchDispatch([punch]));
  }

  private static CompetitorStatus GetCompetitorStatus(InfoExtEnum info) => info switch
  {
    InfoExtEnum.DSQ => CompetitorStatus.DSQ,
    InfoExtEnum.DNS => CompetitorStatus.DNS,
    InfoExtEnum.DNF => CompetitorStatus.DNF,
    _ => CompetitorStatus.Unknown
  };

  private static PunchControlType GetControlType(int logicalChannel) => logicalChannel switch
  {
    0 => PunchControlType.Start,
    byte.MaxValue => PunchControlType.Finish,
    ushort.MaxValue => PunchControlType.Finish,
    _ => PunchControlType.Control
  };
}

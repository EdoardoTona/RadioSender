using Microgate.Common.Protocol.Rei2;
using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RadioSender.Hosts.Source.Microplus;

public class MicrogateSource : TcpClient, ISource, IHostedService, IDisposable
{
  public readonly FilterService _filterService;
  public readonly DispatcherService _dispatcherService;
  public readonly MicrogateSourceConfiguration _configuration;
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
    Log.Information("MicrogateSource {address}:{port} connected", Address.ToString(), Port);
  }

  protected override void OnDisconnected()
  {
    Log.Information("MicrogateSource {address}:{port} disconnected", Address.ToString(), Port);

    // Wait for a while...
    Thread.Sleep(1000);

    // Try to connect again
    if (!_stop)
      ConnectAsync();
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

    if (type != Rei2MsgTypes.Rei2ExtData)
    {
      Log.Warning("MicrogateSource received invalid message type: {type}", type);
      return;
    }

    var data = new Rei2ExtData(b);

    if (data.CompetitorNumber == null || data.Timestamp == null || data.IsNetTime)
      return;

    var punch = _filterService.Transform(
                   _configuration.Filter,
                    new Punch(
                     ReceivedAt: DateTimeOffset.UtcNow,
                    Card: data.CompetitorNumber.Value.ToString(),
                    Control: data.LogicalChannel,
                    ControlType: data.LogicalChannel == 0 ? PunchControlType.Start :
                          data.LogicalChannel == byte.MaxValue || data.LogicalChannel == ushort.MaxValue ? PunchControlType.Finish :
                          PunchControlType.Unknown,
                    Time: data.Timestamp.Value,
                    SourceId: "Microgate", // TODO
                    Cancellation: data.Info == InfoExtEnum.Annulled,
                    CompetitorStatus: CompetitorStatus.Unknown
                    )
                 );

    if (punch != null)
      _dispatcherService.PushDispatch(new PunchDispatch([punch]));
  }

}

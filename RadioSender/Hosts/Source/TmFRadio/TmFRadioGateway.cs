using Microsoft.Extensions.Hosting;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Protocol.TmF;
using RJCP.IO.Ports;
using Serilog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public sealed class TmFRadioGateway : ISource, IRadioSenderHost, IAsyncDisposable
  {
    public const uint BROADCAST = 0xffffffff;

    private readonly IDispatchSink _dispatcherService;
    private readonly Gateway _configuration;
    private readonly SerialPortStream _port;
    //private readonly SerialPortStream _serialPort;
    private readonly CancellationTokenSource _cts = new();

    private Task? _readTask;
    private byte _commandId;
    private Task? _pollTask;

    private bool disposed;

    public TmFRadioGateway(
      IDispatchSink dispatcherService,
      Gateway configuration)
    {
      _dispatcherService = dispatcherService;
      _configuration = configuration;
      _port = new SerialPortStream(_configuration.PortName, _configuration.Baudrate, 8, Parity.None, StopBits.One)
      {
        //_port.PortName = _configuration.PortName!;
        //_port.BaudRate = _configuration.Baudrate;
        //_port.Parity = Parity.None;
        //_port.StopBits = StopBits.One;
        //_port.Handshake = Handshake.None;
        //_port.DataBits = 8;
        WriteTimeout = 500,
        ReadTimeout = 500,
        RtsEnable = true,
        DtrEnable = true
      };

      //_port.ErrorReceived += _port_ErrorReceived;
      //_port.DataReceived += _port_DataReceived;

    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
      _dispatcherService.Logger.Verbose("Initialized " + _configuration.PortName);
      await OpenSerialPort();
      if (!_port.IsOpen) throw new IOException("Unable to open the TmF serial port.");
      _readTask = Task.Run(ReadData, CancellationToken.None);
      _pollTask = PollStatusAsync();
    }

    private async Task PollStatusAsync()
    {
      try
      {
        while (!_cts.IsCancellationRequested)
        {
          await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _configuration.StatusCheck / 2)), _cts.Token);
          await CheckStatus();
          await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _configuration.StatusCheck / 2)), _cts.Token);
          await CheckPath();
        }
      }
      catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
      catch (Exception e) { _dispatcherService.Logger.Warning(e, "Radio status polling stopped."); }
    }

    private Task OpenSerialPort()
    {
      try
      {
        _port.Close();

        _port.OpenDirect();



        _dispatcherService.Logger.Information("Tmf Radio port {port} connected", _configuration.PortName);
      }
      catch (UnauthorizedAccessException)
      {
        _dispatcherService.Logger.Error("Tmf Radio port {port} occupied by another program", _configuration.PortName);
      }
      catch (FileNotFoundException)
      {
        _dispatcherService.Logger.Error("Tmf Radio port {port} not found", _configuration.PortName);
      }
      catch (IOException e)
      {
        if (e.Message.Contains("Port not found"))
        {
          _dispatcherService.Logger.Error("Tmf Radio port {port} not found", _configuration.PortName);
        }
        else
        {
          _dispatcherService.Logger.Error(e, "Tmf Radio error opening the serial port {port}", _configuration.PortName);
        }
      }
      catch (Exception e)
      {
        _dispatcherService.Logger.Error(e, "Tmf Radio error starting port {port}", _configuration.PortName);
      }

      return Task.CompletedTask;
    }



    public async Task StopAsync(CancellationToken cancellationToken)
    {
      if (disposed)
        return;

      disposed = true;

      _cts.Cancel();

      if (_port.IsOpen) { _port.DtrEnable = false; _port.Close(); }

      if (_readTask != null)
        await _readTask;

      if (_pollTask != null) await _pollTask;
      _port.Dispose();
      _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
      await StopAsync(default);
    }

    public async Task CheckPathAndStatus(bool delay = false, CancellationToken ct = default)
    {
      if (!_port.IsOpen) throw new IOException("The TmF gateway is not connected.");
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
      if (delay) await Task.Delay(2000, linked.Token);
      await SendData(GenerateCommand(TmFCommand.GetStatus), linked.Token);
      await Task.Delay(2000, linked.Token);
      await SendData(GenerateCommand(TmFCommand.GetPacketPath), linked.Token);
    }

    public Task CheckStatus() => SendData(GenerateCommand(TmFCommand.GetStatus), _cts.Token);
    public Task CheckPath() => SendData(GenerateCommand(TmFCommand.GetPacketPath), _cts.Token);

    public ReadOnlyMemory<byte> GenerateCommand(TmFCommand command, uint address = BROADCAST, byte arg0 = 0x00, byte arg1 = 0x00)
    {
      Span<byte> b = stackalloc byte[4];

      BinaryPrimitives.WriteUInt32LittleEndian(b, address);

      ReadOnlyMemory<byte> data = new byte[] { 10,           // length of the command
                                               b[3],         // 1 address
                                               b[2],         // 2 address
                                               b[1],         // 3 address
                                               b[0],         // 4 address
                                               ++_commandId, // Command Number
                                               0x03,         // Packet Type fix 3
                                               (byte)command,// Command Argument 17 = Get Status
                                               arg0,         // Data1
                                               arg1,         // Data2
                                               };
      return data;
    }

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    public async Task SendData(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
      await _writeGate.WaitAsync(ct);
      try
      {
        if (!_port.IsOpen) throw new IOException("The TmF gateway is not connected.");
        await _port.WriteAsync(data, ct);
      }
      finally { _writeGate.Release(); }
    }

    private async Task ReadData()
    {
      try
      {
        await Task.Yield();
        while (!_cts.Token.IsCancellationRequested)
        {
          if (!_port.IsOpen)
          {
            await Task.Delay(2000, _cts.Token);
            await OpenSerialPort();
            continue;
          }

          try
          {
            var length = _port.ReadByte();

            if (length <= 0)
              continue;

            if (_port.BytesToRead < length - 1)
            {
              await Task.Delay(150, _cts.Token);
              if (_port.BytesToRead < length - 1)
              {
                length = (byte)_port.BytesToRead;
                _dispatcherService.Logger.Warning("{port} Expected {length} bytes, reading {bytesToRead}", _configuration.PortName, length - 1, _port.BytesToRead);
              }
            }

            if (length <= 0)
              continue;

            var data = new byte[length];
            data[0] = (byte)Math.Min(byte.MaxValue, length);

            await _port.ReadAsync(data.AsMemory(1, length - 1), _cts.Token).ConfigureAwait(false);

            ProcessReceivedMessage(data);
          }
          catch (TimeoutException) { }
          catch (OperationCanceledException)
          {
            _dispatcherService.Logger.Warning("{port} Connection lost from the serial port", _configuration.PortName);
          }
          catch (Exception e)
          {
            _dispatcherService.Logger.Error(e, "{port} Exeption reading data from serial port", _configuration.PortName);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // quiet
      }
    }

    private void ProcessReceivedMessage(byte[] data)
    {
      var dispatch = TmFProtocol.MessageToDispatch(data, out var message, out var serialText, out var error);

      if (error != null)
      {
        _dispatcherService.Logger.Error("{port} {error}: {hex}", _configuration.PortName, error, BitConverter.ToString(data));
        return;
      }

      switch (message)
      {
        case RxGetStatus packet:
          _dispatcherService.Logger.Verbose("{port} Source {source} {msg}: signal {rssi:0}% ({latency:0}ms), {temperature:0}°, {voltage:0.00}V", _configuration.PortName, packet.Header.OrigID, packet.EventDetailString, packet.Header.RSSI_Percent, packet.Header.Latency, packet.Temperat_C, packet.Voltage_V);
          break;

        case RxGetPath packet:
        {
          var nodes = dispatch?.Nodes ?? Array.Empty<NodeNew>();
          _dispatcherService.Logger.Verbose("{port} Source {source} has {hops} hops (nodes: {nodes})", _configuration.PortName, packet.Header.OrigID, packet.Jumps.Count, string.Join('-', nodes.Select(n => n.Id)));
          break;
        }

        case RxData packet when dispatch?.Punches == null:
        {
          if (serialText == null)
            return;

          if (TmFProtocol.HasNotPrintableChars(packet.RxSerData))
            _dispatcherService.Logger.Verbose("{port} Source {source} says: {ascii} [HEX: {hex}]", _configuration.PortName, packet.Header.OrigID, serialText, BitConverter.ToString(data));
          else
            _dispatcherService.Logger.Information("{port} Source {source} says: {ascii}", _configuration.PortName, packet.Header.OrigID, serialText);

          return;
        }
      }

      if (dispatch == null)
        return;

      if (dispatch.Punches != null)
      {
        var punches = dispatch.Punches.ToArray();
        if (punches.Length == 0)
          return;

        dispatch = dispatch with { Punches = punches };
      }

      _dispatcherService.PushDispatch(dispatch);
    }
  }
}

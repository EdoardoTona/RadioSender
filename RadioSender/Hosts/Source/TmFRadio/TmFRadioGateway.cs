using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Source.SportidentSerial;
using RJCP.IO.Ports;
using Serilog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public sealed class TmFRadioGateway : ISource, IHostedService, IDisposable
  {
    public const uint BROADCAST = 0xffffffff;

    private readonly IFilter _filter;
    private readonly DispatcherService _dispatcherService;
    private readonly Gateway _configuration;
    private SerialPortStream _port;
    //private readonly SerialPortStream _serialPort;
    private readonly CancellationTokenSource _cts = new();

    private Task? _readTask;
    private byte _commandId;
    private Timer? _timer_path;
    private Timer? _timer_status;

    private bool disposed = false;

    public TmFRadioGateway(
      IEnumerable<IFilter> filters,
      DispatcherService dispatcherService,
      Gateway gateway)
    {
      _dispatcherService = dispatcherService;
      _configuration = gateway;
      _filter = filters.GetFilter(_configuration.Filter);
      _port = new SerialPortStream(_configuration.PortName, _configuration.Baudrate, 8,Parity.None,StopBits.One);
      //_port.PortName = _configuration.PortName!;
      //_port.BaudRate = _configuration.Baudrate;
      //_port.Parity = Parity.None;
      //_port.StopBits = StopBits.One;
      //_port.Handshake = Handshake.None;
      //_port.DataBits = 8;
      _port.WriteTimeout = 500;
      _port.RtsEnable = true;
      _port.DtrEnable = true;

            //_port.ErrorReceived += _port_ErrorReceived;
            //_port.DataReceived += _port_DataReceived;

      _dispatcherService.RequestPing += _dispatcherService_RequestPing;
    }

    private void _dispatcherService_RequestPing(object? sender, EventArgs e)
    {
      try
      {
          _ = CheckPathAndStatus(delay:false);
      }
      catch
      {
          // quiet
      }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
      await OpenSerialPort();
      _readTask = ReadData();
      _timer_status = new Timer((state) => _ = CheckStatus(), null, _configuration.StatusCheck * 1000, _configuration.StatusCheck * 1000);
      _timer_path = new Timer((state) => _ = CheckPath(), null, (_configuration.StatusCheck / 2) * 1000, _configuration.StatusCheck * 1000);
    }

    private Task OpenSerialPort()
    {
      try
      {
        _port.Close();

        _port.OpenDirect();

        _ = CheckPathAndStatus(delay:true);

        Log.Information("Port {port} connected", _configuration.PortName);
      }
      catch (UnauthorizedAccessException e)
      {
        Log.Error("Port {port} occupied by another program", _configuration.PortName);
      }
      catch (FileNotFoundException e)
      {
        Log.Error("Port not found {port}", _configuration.PortName);
      }
      catch (IOException e)
      {
        if(e.Message.Contains("Port not found"))
        {
          Log.Error("Port not found {port}", _configuration.PortName);
        }
        else
        {
          Log.Error(e, "Error opening the serial port {port}", _configuration.PortName);
        }
      }
      catch (Exception e)
      {
        Log.Error(e, "Error starting port {port}", _configuration.PortName);
      }

      return Task.CompletedTask;
    }



    public async Task StopAsync(CancellationToken cancellationToken)
    {
      if (disposed)
        return;

      disposed = true;

      _timer_path?.Dispose();
      _timer_status?.Dispose();
      _cts?.Cancel();

      _port.DtrEnable = false;

      if (_port.IsOpen)
        _port.Close();

      if (_readTask != null)
        await _readTask;

      _port.Dispose();
    }

    public void Dispose()
    {
      StopAsync(default).Wait();
    }

    public async Task CheckPathAndStatus(bool delay = false)
    {
      try
      {
        if(delay)
          await Task.Delay(2000, _cts.Token);

        await CheckStatus();
        await Task.Delay(2000, _cts.Token);
        await CheckPath();
      }
      catch (Exception e)
      {
        Log.Error(e, "Check status and path  Exception");
      }
    }

    public async Task CheckStatus()
    {
      try
      {
        Log.Verbose("Check status requested");
        await SendData(GenerateCommand(TmFCommand.GetStatus), _cts.Token);
      }
      catch (Exception e)
      {
        Log.Error(e, "Check Status Exception");
      }
    }

    public async Task CheckPath()
    {
      try
      {
        Log.Verbose("Check path requested");
        await SendData(GenerateCommand(TmFCommand.GetPacketPath), _cts.Token);
      }
      catch (Exception e)
      {
        Log.Error(e, "Check Path Exception");
      }
    }

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

    public async Task SendData(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
      if (!_port.IsOpen)
        return;

      try
      {
        //await _port.BaseStream.WriteAsync(data, ct);
        await _port.WriteAsync(data, ct);
      }
      catch (TimeoutException)
      {
        // quiet
      }
      catch (Exception e)
      {
        Log.Error("Error sending command SportidentSerial {msg}", e.Message);
      }
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
                length = (byte) _port.BytesToRead;
                Log.Warning("Expected {length} bytes, reading {bytesToRead}", length - 1, _port.BytesToRead);
              }
            }

            if (length <= 0)
              continue;

            var data = new byte[length];
            data[0] = (byte)Math.Min(byte.MaxValue, length);

            await _port.ReadAsync(data,1,length - 1, _cts.Token).ConfigureAwait(false);

            ProcessReceivedMessage(data);
          }
          catch (OperationCanceledException)
          {
            Log.Warning("Connection lost from the serial port");
          }
          catch (Exception e)
          {
            Log.Error(e, "Exeption reading data from serial port");
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

      int length = data.Length;
      if (length < 18)
      {
        Log.Error("Received broken message of {count} bytes: {hex}", length, BitConverter.ToString(data));
        return;
      }

      var header = new RxHeader(data);



      if (header.PacketType == PacketType.Event)
      {
        if (data[17] == 0x09)
        {
          var packet = new RxGetStatus(header, data);
          Log.Verbose("Source {source} {msg}: signal {rssi:0}% ({latency:0}ms), {temperature:0}°, {voltage:0.00}V", header.OrigID, packet.EventDetailString, header.RSSI_Percent, header.Latency, packet.Temperat_C,packet.Voltage_V);
          _dispatcherService.PushDispatch(new PunchDispatch(Nodes: new[] { new NodeNew(header.OrigID.ToString(), null, header.Latency, header.RSSI_Percent) }));
        }
        else if (data[17] == 0x20)
        {
          var packet = new RxGetPath(header, data);
          var from = header.OrigID;
          var hopsCount = header.HopCounter == 0 ? 1 : header.HopCounter;

          var hops = new List<Hop>();
          var nodes = new List<NodeNew>()
                {
                  new NodeNew(from.ToString(), null, header.Latency, header.RSSI_Percent)
                };
          int i = 1;
          foreach (var jump in (packet as RxGetPath)!.Jumps)
          {
            hops.Add(new Hop(from.ToString(), jump.ReceiverId.ToString(), header.Latency / hopsCount, jump.RSSI_Percent));
            nodes.Add(new NodeNew(jump.ReceiverId.ToString(), null, header.Latency - ((header.Latency / hopsCount) * i), jump.RSSI_Percent));
            from = jump.ReceiverId;
            i++;
          }

          Log.Verbose("Source {source} has {hops} hops (nodes: {nodes})", header.OrigID, hops.Count, string.Join('-',nodes.Select(n => n.Id)));
         _dispatcherService.PushDispatch(new PunchDispatch(Hops: hops, Nodes: nodes));
        }
        else
        {
          // do nothing
        }
      }
      else
      {
        var packet = new RxData(header, data);

        var sportidentMsg = SportidentSerialPort.MessageToPunch(packet.RxSerData, header.OrigID.ToString());

        if(sportidentMsg == null)
        {
          if (HasNotPrintableChars(packet.RxSerData))
            Log.Verbose("Source {source} says: {ascii} [HEX: {hex}]", header.OrigID, Encoding.ASCII.GetString(packet.RxSerData), BitConverter.ToString(data));
          else
            Log.Information("Source {source} says: {ascii}", header.OrigID, Encoding.ASCII.GetString(packet.RxSerData));

          return;
        }

        var punch = _filter.Transform(sportidentMsg);

        if (punch != null)
          _dispatcherService.PushDispatch(new PunchDispatch(Punches: new[] { punch }));
      }
    }

    private bool HasNotPrintableChars(byte[] inputList) =>
    inputList.Any(s => s != 0x0D && s != 0x0A && (s < 0x20 || s > 0x7E));
  }
}

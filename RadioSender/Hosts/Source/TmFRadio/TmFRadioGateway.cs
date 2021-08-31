using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Source.SportidentSerial;
using Serilog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public sealed class TmFRadioGateway : ISource, IHostedService, IDisposable
  {
    public const uint BROADCAST = 0xffffffff;

    private readonly IFilter _filter = Filter.Invariant;
    private readonly DispatcherService _dispatcherService;
    private readonly Gateway _configuration;
    private readonly SerialPort _port;
    private readonly CancellationTokenSource _cts = new();

    private Task? _readTask;
    private byte _commandId;
    private Timer? _timer;

    public TmFRadioGateway(
      IEnumerable<IFilter> filters,
      DispatcherService dispatcherService,
      Gateway gateway)
    {
      _dispatcherService = dispatcherService;
      _configuration = gateway;
      _port = new SerialPort();
      _filter = filters.GetFilter(_configuration.Filter);
    }

    public Task StartAsync(CancellationToken st)
    {
      try
      {
        if (_port.IsOpen)
          _port.Close();

        _port.PortName = _configuration.PortName;
        _port.BaudRate = _configuration.Baudrate;
        _port.Parity = Parity.None;
        _port.StopBits = StopBits.One;
        _port.Handshake = Handshake.None;
        _port.DataBits = 8;
        _port.WriteTimeout = 500;
        _port.RtsEnable = true;
        _port.DtrEnable = true;
        _port.Open();

        _readTask = ReadData();

        _timer = new Timer(CheckStatus, null, 0, 5000);

        //CheckStatus, null, 0, 5000);
        return Task.CompletedTask;
      }
      catch (UnauthorizedAccessException e)
      {
        Log.Error("Port {port} occupied by another program", _configuration.PortName);
        return Task.FromException(e);
      }
      catch (FileNotFoundException e)
      {
        Log.Error("Port {port} doesn't exist", _configuration.PortName);
        return Task.FromException(e);
      }
      catch (IOException)
      {
        throw;
      }
      catch (Exception e)
      {
        Log.Error(e, "Error starting port {port}", _configuration.PortName);
        return Task.FromException(e);
      }
    }


    public async Task StopAsync(CancellationToken st)
    {
      _timer?.Dispose();
      _cts?.Cancel();

      if (_port == null)
        return;

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

    public async void CheckStatus(object? state)
    {
      await SendData(GenerateCommand(TmFCommand.GetStatus), _cts.Token);
      await Task.Delay(200, _cts.Token);
      await SendData(GenerateCommand(TmFCommand.GetPacketPath), _cts.Token);
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
      if (!_port?.IsOpen ?? false)
        return;

      try
      {
        //_port.Write(data, 0, data.Length);
        await _port!.BaseStream.WriteAsync(data, ct);
      }
      catch (TimeoutException)
      {
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
        while (!_cts.Token.IsCancellationRequested)
        {
          if (!_port?.IsOpen ?? false)
          {
            await Task.Delay(5000, _cts.Token);
            // TODO try to reopen
            continue;
          }

          try
          {
            var length = (int)await _port!.ReadByteAsync(_cts.Token).ConfigureAwait(false);

            if (_port!.BytesToRead < length - 1)
            {
              await Task.Delay(50, _cts.Token);
              if (_port.BytesToRead < length - 1)
              {
                length = _port.BytesToRead;
              }
            }

            if (length <= 0)
              continue;

            var buffer = await _port.ReadAsync(length - 1, _cts.Token).ConfigureAwait(false);

            var data = new byte[length];
            data[0] = (byte)Math.Min(byte.MaxValue, length);
            Buffer.BlockCopy(buffer, 0, data, 1, buffer.Length); // TODO optimize this part (using a memorystream?)

            Log.Verbose(BitConverter.ToString(data));

            if (length < 18)
            {
              Log.Error("Received broken message of {count} bytes", length);
              continue;
            }

            var header = new RxHeader(data);


            RxMsg? packet = null;

            if (header.PacketType == PacketType.Event)
            {
              if (data[17] == 0x09)
              {
                packet = new RxGetStatus(header, data);
                //UpdateNode(header.OrigID, header.RSSI_Percent, (packet as RxGetStatus)!.Voltage_V);
                _dispatcherService.PushDispatch(new PunchDispatch(Nodes: new[] { new NodeNew(header.OrigID.ToString(), null, header.Latency, header.RSSI_Percent) }));
              }
              else if (data[17] == 0x20)
              {
                packet = new RxGetPath(header, data);
                var from = header.OrigID;
                var hop = header.HopCounter == 0 ? 1 : header.HopCounter;

                var list = new List<Hop>();
                foreach (var jump in (packet as RxGetPath)!.Jumps)
                {
                  list.Add(new Hop(from.ToString(), jump.ReceiverId.ToString(), header.Latency / hop, jump.RSSI_Percent));
                  //UpdateEdge(from, jump.ReceiverId, jump.RSSI_Percent, header.Latency / hop);
                  from = jump.ReceiverId;
                }

                _dispatcherService.PushDispatch(new PunchDispatch(Hops: list));
              }
              else
              {

              }
            }
            else
            {
              packet = new RxData(header, data);

              var punch = _filter.Transform(SportidentSerialPort.MessageToPunch((packet as RxData)!.RxSerData));

              if (punch != null)
                _dispatcherService.PushDispatch(new PunchDispatch(Punches: new[] { punch }));
            }

            Log.Verbose(packet?.ToString());
          }
          catch (Exception e)
          {
            Log.Error(e, "Exeption reading data from serial port");
          }
        }
      }
      catch (OperationCanceledException)
      {

      }

    }

    //public void UpdateNode(uint address, int signal, double battery)
    //{
    //  _deviceService.UpdateNode(new Node("TmF" + address, "TmF" + address, signal / 10, $"Battery: {battery:0.00}V", DateTimeOffset.UtcNow));

    //}

    //public void UpdateEdge(uint from, uint to, int signal, int latency)
    //{
    //  _deviceService.UpdateEdge(new Edge("TmF" + from, "TmF" + to, signal / 10, latency * 2, signal + "%", "", DateTimeOffset.UtcNow));
    //}
  }
}

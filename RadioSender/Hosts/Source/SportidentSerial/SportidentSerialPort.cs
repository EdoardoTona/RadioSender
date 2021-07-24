using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SportidentSerial
{
  public class SportidentSerialPort : IHostedService, IDisposable
  {
    private const byte WAKEUP = 0xFF;
    private const byte STX = 0x02;
    private const byte ETX = 0x03;
    private const byte ACK = 0x06;
    private const byte NAK = 0x15;

    private const byte CMD_ExtendSetMsMode = 0xF0;
    private const byte CMD_SetMsMode = 0x70;
    private const byte CMD_GetSystemValue = 0x83;
    private const byte CMD_TransmitRecord = 0xD3;

    private const byte ARG_DirectCommunication = 0x4D;
    private const byte ARG_RemoteCommunication = 0x53;

    private readonly DispatcherService _dispatcherService;
    private readonly Port _portInfo;
    private readonly SerialPort _port;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private SportidentProduct _stationInfo;
    private Task _readTask;

    public SportidentSerialPort(DispatcherService dispatcherService, Port portInfo)
    {
      _dispatcherService = dispatcherService;
      _portInfo = portInfo;
      _port = new SerialPort();
    }

    public async Task StartAsync(CancellationToken st)
    {
      try
      {
        if (_port.IsOpen)
          _port.Close();

        _port.PortName = _portInfo.PortName;
        _port.BaudRate = 38400;
        //_port.RtsEnable = true;
        _port.DtrEnable = true;
        _port.Parity = Parity.None;
        _port.StopBits = StopBits.One;
        _port.Handshake = Handshake.None;
        _port.DataBits = 8;
        _port.WriteTimeout = 500;
        _port.Open();

        var unknownStation = false;
        // try BSM7/8 at 38400 baud
        var res = await SendCommand(CMD_ExtendSetMsMode, ARG_DirectCommunication);
        if (res == null)
        {
          _port.BaudRate = 4800;
          // try BSM7/8 at 4800 baud
          res = await SendCommand(CMD_ExtendSetMsMode, ARG_DirectCommunication);
          if (res == null || res.Last() == NAK)
          {
            // BSM3/4/6 at 4800 baud with old protocol (unsupported)
            unknownStation = true;
            Log.Warning("Unable to get station info");
          }
        }

        if (!unknownStation)
        {
          _stationInfo = await GetStationInfo();
          Log.Information("Port {port} baudrate {baudrate} device: {info}", _portInfo.PortName, _port.BaudRate, _stationInfo);
        }
        else
        {
          Log.Information("Port {port} baudrate {baudrate} device unknown", _portInfo.PortName, _port.BaudRate);
        }

        _readTask = ReadData();
      }
      catch (UnauthorizedAccessException)
      {
        Log.Error("Port {port} occupied by another program", _portInfo.PortName);
      }
      catch (FileNotFoundException)
      {
        Log.Error("Port {port} doesn't exist", _portInfo.PortName);
      }
      catch (IOException)
      {
        throw;
      }
      catch (Exception e)
      {
        Log.Error(e, "Error starting port {port}", _portInfo.PortName);
      }
    }

    public async Task StopAsync(CancellationToken st)
    {
      _cts.Cancel();

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


    private async Task<SportidentProduct> GetStationInfo()
    {
      var res = await SendCommand(CMD_GetSystemValue, 0x00, 0x80); // read 0x80 bytes from position 0x00

      using var ms = new MemoryStream(res);

      while (ms.Position < ms.Length)
      {
        var value = ms.ReadByte();

        if (value != STX)
          continue;

        var cmd = (byte)ms.ReadByte();
        if (cmd != CMD_GetSystemValue)
          continue;

        var length = (byte)ms.ReadByte();

        byte[] data = new byte[length];
        ms.Read(data, 0, length);

        var myCrc = CalculateCrc(cmd, data);

        byte[] crc = new byte[2];
        ms.Read(crc, 0, 2);

        if (!myCrc.SequenceEqual(crc))
        {
          Log.Warning("CRC Error");
          return null;
        }

        var etx = ms.ReadByte(); // should be ETX

        return SportidentStationInfo.GetSportidentProductInfo(data);
      }

      return null;
    }


    private async Task ReadData()
    {
      while (!_cts.Token.IsCancellationRequested)
      {
        try
        {
          if (await _port.ReadByteAsync(_cts.Token).ConfigureAwait(false) != STX)
            continue;

          var cmd = await _port.ReadByteAsync(_cts.Token).ConfigureAwait(false);
          if (cmd != CMD_TransmitRecord)
            continue;

          // STX + CMD + LEN (3 bytes)

          var length = await _port.ReadByteAsync(_cts.Token).ConfigureAwait(false);

          var buffer = await _port.ReadAsync(length + 3, _cts.Token).ConfigureAwait(false); // read length (13) + CRC1 + CRC2 + ETX

          var data = new byte[length + 6];
          data[0] = STX;
          data[1] = CMD_TransmitRecord;
          data[2] = length;
          Buffer.BlockCopy(buffer, 0, data, 3, buffer.Length);

          _dispatcherService.PushPunch(MessageToPunch(data));
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception e)
        {
          Log.Error(e, "Exeption reading data from serial port");
        }
      }

    }

    public async Task<byte[]> SendCommand(byte command, params byte[] parameters)
    {
      var crc = CalculateCrc(command, parameters);

      try
      {
        using (var ms = new MemoryStream())
        {
          ms.WriteByte(WAKEUP);
          ms.WriteByte(STX);
          ms.WriteByte(command);
          ms.WriteByte((byte)parameters.Length);
          ms.Write(parameters);
          ms.Write(crc);
          ms.WriteByte(ETX);
          var data = ms.ToArray();
          Log.Verbose("SEND: " + BitConverter.ToString(data));
          _port.Write(data, 0, data.Length);
        }

        await Task.Delay(500, _cts.Token);

        if (_port.BytesToRead > 0)
        {
          var buffer = new byte[_port.BytesToRead];
          _port.Read(buffer, 0, buffer.Length);
          Log.Verbose("RECEIVED: " + BitConverter.ToString(buffer));
          return buffer;
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (TimeoutException)
      {

      }
      catch (Exception e)
      {
        Log.Error("Error sending command SportidentSerial {msg}", e.Message);
      }
      return null;
    }

    public static Punch MessageToPunch(byte[] buffer)
    {
      if (buffer[0] == WAKEUP)
        Buffer.BlockCopy(buffer, 1, buffer, 0, buffer.Length - 1);

      if (buffer[0] != STX)
      {
        Log.Warning("Wrong STX");
        return null;
      }

      if (buffer[1] != CMD_TransmitRecord)
      {
        Log.Warning("Wrong CMD");
        return null;
      }

      var length = buffer[2];

      var crcData = new byte[length + 2];
      Buffer.BlockCopy(buffer, 1, crcData, 0, crcData.Length);

      var crc = new byte[2];
      Buffer.BlockCopy(buffer, crcData.Length + 1, crc, 0, 2);

      if (!CalculateCrc(crcData).SequenceEqual(crc))
      {
        Log.Warning("CRC Error");
        return null;
      }

      var controlCode = BinaryPrimitives.ReadUInt16BigEndian(new byte[] { (byte)(buffer[3] & 0b_0111_1111), buffer[4] });

      int cardNumber;
      if (buffer[6] > 0x04)
        cardNumber = (int)BinaryPrimitives.ReadUInt32BigEndian(new byte[] { 0, buffer[6], buffer[7], buffer[8] });
      else
      {
        cardNumber = BinaryPrimitives.ReadUInt16BigEndian(new byte[] { buffer[7], buffer[8] }) + buffer[6] * 100000;
      }

      var am = buffer[9] % 2 == 0; // antemeridian
      var dayOfWeek = (buffer[9] << 4) >> 5; // from 0 (sunday) to 6 (saturday)

      var time_s = BinaryPrimitives.ReadUInt16BigEndian(new byte[] { buffer[10], buffer[11] });

      var subseconds = buffer[12] / 256d;

      var time = TimeSpan.FromSeconds(time_s + subseconds);
      if (!am)
        time += TimeSpan.FromHours(12);

      var now = DateTime.Now;
      var dt = new DateTime(now.Year, now.Month, now.Day) + time;

      return new Punch()
      {
        Card = cardNumber.ToString(),
        Time = dt,
        Control = controlCode,
        OriginalControlType = PunchControlType.Unknown
      };
    }


    public static byte[] CalculateCrc(byte command, byte[] data, byte length = 0)
    {
      length = length == 0 ? (byte)data.Length : length;
      using var ms = new MemoryStream();
      ms.WriteByte(command);
      ms.WriteByte(length);
      ms.Write(data);
      return CalculateCrc(ms.ToArray());
    }

    public static byte[] CalculateCrc(byte[] data)
    {
      var crcBytes = new byte[2];
      // Return 0 for no or on-e data byte
      if (data.Length < 2)
      {
        return crcBytes;
      }

      uint index = 0;
      ushort crc = (ushort)((data[index] << 8) + data[index + 1]);
      index += 2;
      // Return crc for two data bytes
      if (data.Length == 2)
      {
        BinaryPrimitives.WriteUInt16BigEndian(crcBytes, crc);
        return crcBytes;
      }

      ushort value;
      for (uint k = (uint)(data.Length >> 1); k > 0; k--)
      {
        if (k > 1)
        {
          value = (ushort)((data[index] << 8) + data[index + 1]);
          index += 2;
        }
        else // If the number of bytes is odd, complete with 0.
        {
          value = (data.Length & 1) != 0 ? (ushort)(data[index] << 8) : (ushort)0;
        }

        for (int j = 0; j < 16; j++)
        {
          if ((crc & 0x8000) != 0)
          {
            crc <<= 1;
            if ((value & 0x8000) != 0)
            {
              crc++;
            }
            crc ^= 0x8005;
          }
          else
          {
            crc <<= 1;
            if ((value & 0x8000) != 0)
            {
              crc++;
            }
          }
          value <<= 1;
        }
      }
      BinaryPrimitives.WriteUInt16BigEndian(crcBytes, crc);
      return crcBytes;
    }
  }
}

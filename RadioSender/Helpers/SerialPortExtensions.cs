using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Helpers
{
  public static class SerialPortExtensions
  {
    // TODO optimize
    public async static Task ReadAsync(this SerialPort serialPort, byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
      var bytesToRead = count;
      var temp = new byte[count];

      while (bytesToRead > 0)
      {
        var readBytes = await serialPort.BaseStream.ReadAsync(temp, 0, bytesToRead, ct);
        Array.Copy(temp, 0, buffer, offset + count - bytesToRead, readBytes);
        bytesToRead -= readBytes;
      }
    }

    public async static Task<byte[]> ReadAsync(this SerialPort serialPort, int count, CancellationToken ct = default)
    {
      var buffer = new byte[count];
      await serialPort.ReadAsync(buffer, 0, count, ct);
      return buffer;
    }

    public async static Task<byte> ReadByteAsync(this SerialPort serialPort, CancellationToken ct = default)
    {
      var buffer = new byte[1];
      await serialPort.BaseStream.ReadAsync(buffer, 0, 1, ct);
      return buffer[0];
    }

  }
}

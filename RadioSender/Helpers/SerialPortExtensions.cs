using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Helpers
{
  public static class SerialPortExtensions
  {
    public async static Task<byte[]> ReadAsync(this SerialPort serialPort, int count, CancellationToken ct = default)
    {
      var buffer = new byte[count];
      await serialPort.BaseStream.ReadExactlyAsync(buffer.AsMemory(0, count), ct);
      return buffer;
    }

    public async static Task<byte> ReadByteAsync(this SerialPort serialPort, CancellationToken ct = default)
    {
      var buffer = new byte[1];
      await serialPort.BaseStream.ReadExactlyAsync(buffer.AsMemory(0, 1), ct);
      return buffer[0];
    }

  }
}

using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Buffers.Binary;

namespace RadioSender.Hosts.Protocol.Sportident;

public static class SportidentProtocol
{
  public const byte Wakeup = 0xFF;
  public const byte Stx = 0x02;
  public const byte Etx = 0x03;
  public const byte CmdTransmitRecord = 0xD3;

  public static Punch? MessageToPunch(byte[] buffer, string sourceId)
  {
    return MessageToPunch(buffer.AsSpan(), sourceId);
  }

  public static Punch? MessageToPunch(
    ReadOnlySpan<byte> buffer,
    string sourceId,
    DateTime? referenceDate = null,
    DateTimeOffset? receivedAt = null)
  {
    if (buffer.Length == 0)
      return null;

    if (buffer[0] == Wakeup)
      buffer = buffer[1..];

    if (buffer.Length < 6 || buffer[0] != Stx)
      return null;

    if (buffer[1] != CmdTransmitRecord)
    {
      Log.Warning("Wrong CMD");
      return null;
    }

    var length = buffer[2];
    var expectedLength = length + 6;
    if (length < 10 || buffer.Length < expectedLength)
    {
      Log.Warning("Invalid SI message length {length}. Received {received} bytes", length, buffer.Length);
      return null;
    }

    if (buffer[expectedLength - 1] != Etx)
    {
      Log.Warning("Invalid ETX byte");
      return null;
    }

    var crcData = buffer.Slice(1, length + 2);
    var crc = buffer.Slice(length + 3, 2);

    if (!CalculateCrc(crcData).AsSpan().SequenceEqual(crc))
    {
      Log.Warning("CRC Error");
      return null;
    }

    Span<byte> controlBytes = stackalloc byte[2];
    controlBytes[0] = (byte)(buffer[3] & 0b_0111_1111);
    controlBytes[1] = buffer[4];
    var controlCode = BinaryPrimitives.ReadUInt16BigEndian(controlBytes);

    int cardNumber;
    if (buffer[6] > 0x04)
    {
      Span<byte> cardBytes = stackalloc byte[4];
      cardBytes[1] = buffer[6];
      cardBytes[2] = buffer[7];
      cardBytes[3] = buffer[8];
      cardNumber = (int)BinaryPrimitives.ReadUInt32BigEndian(cardBytes);
    }
    else
    {
      cardNumber = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(7, 2)) + buffer[6] * 100000;
    }

#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
    var am = buffer[9] % 2 == 0; // antemeridian
    var dayOfWeek = (buffer[9] << 4) >> 5; // from 0 (sunday) to 6 (saturday)
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore

    var time_s = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(10, 2));
    var subseconds = buffer[12] / 256d;

    var time = TimeSpan.FromSeconds(time_s + subseconds);
    if (!am)
      time += TimeSpan.FromHours(12);

    var day = (referenceDate ?? DateTime.Now).Date;
    var dt = day + time;

    return new Punch(
      ReceivedAt: receivedAt ?? DateTimeOffset.UtcNow,
      Card: cardNumber.ToString(),
      Time: dt,
      Control: controlCode,
      ControlType: PunchControlType.Unknown,
      SourceId: sourceId
    );
  }

  public static byte[] CalculateCrc(byte command, byte[] data, byte length = 0)
  {
    return CalculateCrc(command, data.AsSpan(), length);
  }

  public static byte[] CalculateCrc(byte command, ReadOnlySpan<byte> data, byte length = 0)
  {
    length = length == 0 ? (byte)data.Length : length;

    var crcData = new byte[length + 2];
    crcData[0] = command;
    crcData[1] = length;
    data[..Math.Min(length, data.Length)].CopyTo(crcData.AsSpan(2));

    return CalculateCrc(crcData);
  }

  public static byte[] CalculateCrc(byte[] data)
  {
    return CalculateCrc(data.AsSpan());
  }

  public static byte[] CalculateCrc(ReadOnlySpan<byte> data)
  {
    var crcBytes = new byte[2];
    // Return 0 for no or one data byte
    if (data.Length < 2)
      return crcBytes;

    var index = 0;
    ushort crc = (ushort)((data[index] << 8) + data[index + 1]);
    index += 2;

    if (data.Length == 2)
    {
      BinaryPrimitives.WriteUInt16BigEndian(crcBytes, crc);
      return crcBytes;
    }

    ushort value;
    for (var k = data.Length >> 1; k > 0; k--)
    {
      if (k > 1)
      {
        value = (ushort)((data[index] << 8) + data[index + 1]);
        index += 2;
      }
      else
      {
        value = (data.Length & 1) != 0 ? (ushort)(data[index] << 8) : (ushort)0;
      }

      for (var j = 0; j < 16; j++)
      {
        if ((crc & 0x8000) != 0)
        {
          crc <<= 1;
          if ((value & 0x8000) != 0)
            crc++;

          crc ^= 0x8005;
        }
        else
        {
          crc <<= 1;
          if ((value & 0x8000) != 0)
            crc++;
        }

        value <<= 1;
      }
    }

    BinaryPrimitives.WriteUInt16BigEndian(crcBytes, crc);
    return crcBytes;
  }
}

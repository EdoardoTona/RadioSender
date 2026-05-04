using NUnit.Framework;
using RadioSender.Hosts.Protocol.Sportident;
using System;
using System.Buffers.Binary;

namespace Test.RadioSender;

public class TestProtocolSportident
{
  [Test]
  public void SportidentMessageToPunchReadsTransmitRecord()
  {
    var referenceDate = new DateTime(2026, 5, 4);
    var receivedAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    var bytes = CreateSportidentMessage();

    var s = Convert.ToBase64String(bytes);

    var punch = SportidentProtocol.MessageToPunch(bytes, "mqtt", referenceDate, receivedAt);

    Assert.That(punch, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(punch!.Card, Is.EqualTo("1234"));
      Assert.That(punch.Control, Is.EqualTo(31));
      Assert.That(punch.SourceId, Is.EqualTo("mqtt"));
      Assert.That(punch.ReceivedAt, Is.EqualTo(receivedAt));
      Assert.That(punch.Time, Is.EqualTo(new DateTime(2026, 5, 4, 21, 45, 59, 125)));
    });
  }

  [Test]
  public void SportidentMessageToPunchAcceptsWakeupByte()
  {
    var punch = SportidentProtocol.MessageToPunch(CreateSportidentMessage(withWakeup: true), "serial");

    Assert.That(punch, Is.Not.Null);
    Assert.That(punch!.Card, Is.EqualTo("1234"));
  }

  [Test]
  public void SportidentMessageToPunchRejectsInvalidCrc()
  {
    var message = CreateSportidentMessage();
    message[4] = 32;

    var punch = SportidentProtocol.MessageToPunch(message, "mqtt");

    Assert.That(punch, Is.Null);
  }

  public static byte[] CreateSportidentMessage(bool withWakeup = false)
  {
    const byte length = 13;
    var frame = new byte[length + 6];

    frame[0] = SportidentProtocol.Stx;
    frame[1] = SportidentProtocol.CmdTransmitRecord;
    frame[2] = length;
    frame[3] = 0x00;
    frame[4] = 31;
    frame[6] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7, 2), 1234);
    frame[9] = 0x01; // PM
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10, 2), 35159);
    frame[12] = 32; // 125 ms

    var crc = SportidentProtocol.CalculateCrc(frame.AsSpan(1, length + 2));
    crc.CopyTo(frame.AsSpan(length + 3));
    frame[^1] = SportidentProtocol.Etx;

    if (!withWakeup)
      return frame;

    var wakeupFrame = new byte[frame.Length + 1];
    wakeupFrame[0] = SportidentProtocol.Wakeup;
    Buffer.BlockCopy(frame, 0, wakeupFrame, 1, frame.Length);
    return wakeupFrame;
  }

}

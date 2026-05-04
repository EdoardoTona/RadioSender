using NUnit.Framework;
using RadioSender.Hosts.Protocol.TmF;
using System;
using System.Buffers.Binary;
using System.Linq;

namespace Test.RadioSender;

public class TestProtocolTmF
{

  [Test]
  public void TmFMessageToDispatchReadsNestedSportidentMessage()
  {
    var sourceId = 123456u;
    var message = CreateTmFSerialDataMessage(sourceId, TestProtocolSportident.CreateSportidentMessage());

    var dispatch = TmFProtocol.MessageToDispatch(message, out var protocolMessage, out var serialText, out var error);

    Assert.That(error, Is.Null);
    Assert.That(serialText, Is.Null);
    Assert.That(protocolMessage, Is.TypeOf<RxData>());
    Assert.That(dispatch, Is.Not.Null);

    var punch = dispatch!.Punches!.Single();
    Assert.Multiple(() =>
    {
      Assert.That(punch.Card, Is.EqualTo("1234"));
      Assert.That(punch.Control, Is.EqualTo(31));
      Assert.That(punch.SourceId, Is.EqualTo(sourceId.ToString()));
    });
  }

  [Test]
  public void TmFMessageToDispatchReadsStatusNode()
  {
    var sourceId = 654321u;
    var message = CreateTmFStatusMessage(sourceId);

    var dispatch = TmFProtocol.MessageToDispatch(message, out var protocolMessage, out var serialText, out var error);

    Assert.That(error, Is.Null);
    Assert.That(serialText, Is.Null);
    Assert.That(protocolMessage, Is.TypeOf<RxGetStatus>());
    Assert.That(dispatch, Is.Not.Null);

    var node = dispatch!.Nodes!.Single();
    Assert.Multiple(() =>
    {
      Assert.That(node.Id, Is.EqualTo(sourceId.ToString()));
      Assert.That(node.LatencyMs, Is.EqualTo(250));
      Assert.That(node.SignalStength, Is.EqualTo(RxHeader.ConvRSSI_Percent(60)));
    });
  }

  private static byte[] CreateTmFSerialDataMessage(uint sourceId, byte[] serialData)
  {
    var message = new byte[18 + serialData.Length];
    message[0] = (byte)message.Length;
    BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(1, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(5, 4), sourceId);
    message[9] = 60;
    message[16] = (byte)PacketType.SerialData;
    message[17] = 0;

    Buffer.BlockCopy(serialData, 0, message, 18, serialData.Length);
    return message;
  }

  private static byte[] CreateTmFStatusMessage(uint sourceId)
  {
    var message = new byte[26];
    message[0] = (byte)message.Length;
    BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(1, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(5, 4), sourceId);
    message[9] = 60;
    BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(14, 2), 250);
    message[16] = (byte)PacketType.Event;
    message[17] = 0x09;
    message[24] = 148;
    message[25] = 120;
    return message;
  }
}

using NUnit.Framework;
using RadioSender.Hosts.Protocol.Sportident;
using RadioSender.Hosts.Source.Mqtt;
using System;

namespace Test.RadioSender;

public class TestMqttSource
{
  [TestCase("15FF02D30D001A001F61FD0D5F51360002E02BF303")]
  [TestCase("15FF02D30D001A000216D70D5F44DB0002D809F103")]
  [TestCase("15FF02D30D001A001F85E40D5F2F0B0002D0DA2C03")]
  public void StripBleAdvertisingPrefixUnwrapsManufacturerSpecificData(string hex)
  {
    var payload = Convert.FromHexString(hex);

    var stripped = MqttSource.StripBleAdvertisingPrefix(payload);

    Assert.That(stripped[0], Is.EqualTo(SportidentProtocol.Stx));
    Assert.That(stripped[1], Is.EqualTo(SportidentProtocol.CmdTransmitRecord));

    var punch = SportidentProtocol.MessageToPunch(stripped.ToArray(), "mqtt");
    Assert.That(punch, Is.Not.Null);
  }

  [Test]
  public void StripBleAdvertisingPrefixLeavesPlainFrameUntouched()
  {
    var payload = TestProtocolSportident.CreateSportidentMessage();

    var stripped = MqttSource.StripBleAdvertisingPrefix(payload);

    Assert.That(stripped.ToArray(), Is.EqualTo(payload));
  }
}

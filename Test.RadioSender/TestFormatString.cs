using NUnit.Framework;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;

namespace Test.RadioSender
{
  public class Tests
  {
    Punch punch;
    [OneTimeSetUp]
    public void Setup()
    {
      punch = new Punch()
      {
        Card = "1234",
        Control = 31,
        ControlType = PunchControlType.Control,
        Time = new System.DateTime(2021, 08, 04, 21, 45, 59, 123)
      };
    }

    [TestCase("1234", "{Card}")]
    [TestCase("31", "{Control}")]
    [TestCase("1234", "{card}")]
    [TestCase("31.00", "{Control:0.00}")]
    [TestCase("1234-31", "{Card}-{Control}")]
    [TestCase("31-1234", "{Control}-{Card}")]
    [TestCase("08/04/2021 21:45:59", "{Time}")]
    [TestCase("1234-31-21.45.59", "{Card}-{Control}-{Time:HH.mm.ss}")]
    [TestCase("1234-31-09.45.59", "{Card}-{Control}-{Time:hh.mm.ss}")]
    [TestCase("1234-31-21:45:59", "{Card}-{Control}-{Time:HH:mm:ss}")]
    [TestCase("1234-31-21:45:59.123", "{Card}-{Control}-{Time:HH:mm:ss.fff}")]
    [TestCase("1234-31-21:45:59,123", "{Card}-{Control}-{Time:HH:mm:ss,fff}")]
    [TestCase("1234;31;21:45:59,123", "{Card};{Control};{Time:HH:mm:ss,fff}")]
    [TestCase("1234-31-2021-08-04T21:45:59,123", "{Card}-{Control}-{Time:yyyy-MM-ddTHH:mm:ss,fff}")]
    [TestCase("1628106359123", "{UnixMs}")]
    [TestCase("1628106359", "{UnixS}")]
    [TestCase("\r\n", "{CRLF}")]
    [TestCase("\r", "{CR}")]
    [TestCase("\n", "{LF}")]
    [TestCase("\r\n", "{CR}{LF}")]
    [TestCase("a\rb\nc", "a{CR}b{LF}c")]
    [TestCase("Control", "{ControlType}")]
    [TestCase("1", "{ControlType:d}")]
    [TestCase("00000001", "{ControlType:x}")]
    [TestCase("", "{invalid}")]
    public void Test1(string expected, string conf)
    {
      var res = FormatStringHelper.GetString(punch, conf);
      Assert.AreEqual(expected, res);

      Assert.Pass();
    }
  }
}
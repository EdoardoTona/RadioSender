using NUnit.Framework;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;

namespace Test.RadioSender;

public class TestFormatString
{
  Punch? punch;

  [OneTimeSetUp]
  public void Setup()
  {
    punch = new Punch(
      CompetitorId: "1234",
      CompetitorIdType: CompetitorIdType.PunchingCard,
      Control: 31,
      SourceId: "roc",
      ReceivedAt: new System.DateTimeOffset(2021, 08, 04, 21, 45, 59, 123, System.TimeSpan.Zero),
      ControlType: PunchControlType.Control,
      Time: new System.DateTime(2021, 08, 04, 21, 45, 59, 123)
      );
  }

  [TestCase("1234", "{Card}")]
  [TestCase("1234", "{CompetitorId}")]
  [TestCase("", "{Bib}")] // punch is a punching card, so {Bib} is empty
  [TestCase("PunchingCard", "{CompetitorIdType}")]
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
  [TestCase("", "")]
  public void Test(string expected, string conf)
  {
    var res = FormatStringHelper.GetString(punch!, conf);
    Assert.AreEqual(expected, res);

    Assert.Pass();
  }

  [TestCase("101", "{Bib}")]         // punch is a bib number
  [TestCase("101", "{CompetitorId}")]
  [TestCase("", "{Card}")]           // ...so {Card} is empty
  public void TestBibNumber(string expected, string conf)
  {
    var bibPunch = new Punch(
      CompetitorId: "101",
      CompetitorIdType: CompetitorIdType.BibNumber,
      Control: 31,
      SourceId: "test",
      ReceivedAt: new System.DateTimeOffset(2021, 08, 04, 21, 45, 59, 123, System.TimeSpan.Zero),
      ControlType: PunchControlType.Control,
      Time: new System.DateTime(2021, 08, 04, 21, 45, 59, 123)
      );

    var res = FormatStringHelper.GetString(bibPunch, conf);
    Assert.AreEqual(expected, res);
  }

  [TestCase("101", "{Bib}")]          // enriched: bib resolved from card
  [TestCase("1234", "{Card}")]        // enriched card value
  [TestCase("1234", "{CompetitorId}")] // raw id stays the card
  [TestCase("John Doe", "{Name}")]
  [TestCase("H21", "{Class}")]
  [TestCase("10:30:00", "{StartTime:HH:mm:ss}")]
  public void TestEnriched(string expected, string conf)
  {
    // a punch that arrived as a punching card, enriched with bib/name/class/start
    var enriched = new Punch(
      CompetitorId: "1234",
      CompetitorIdType: CompetitorIdType.PunchingCard,
      Control: 31,
      SourceId: "roc",
      ReceivedAt: new System.DateTimeOffset(2021, 08, 04, 21, 45, 59, 123, System.TimeSpan.Zero),
      ControlType: PunchControlType.Control,
      Time: new System.DateTime(2021, 08, 04, 21, 45, 59, 123),
      Competitor: new Competitor(
        Bib: "101",
        Card: "1234",
        Name: "John Doe",
        Class: "H21",
        StartTime: new System.DateTime(2021, 08, 04, 10, 30, 0))
      );

    var res = FormatStringHelper.GetString(enriched, conf);
    Assert.AreEqual(expected, res);
  }

  [TestCase("{Cancellation}", "Cancellation", true)]
  [TestCase("{cancellation}", "Cancellation", true)] // case-insensitive
  [TestCase("{Card};{Cancellation};{Status}", "Cancellation", true)]
  [TestCase("{Card};{Status}", "Cancellation", false)]
  [TestCase("", "Cancellation", false)]
  [TestCase("{Status}", "Status", true)]
  [TestCase("{Time:HH:mm:ss}", "Status", false)]
  [TestCase("{CompetitorId};{Control};{Time:HH:mm:ss,fff}", "Time", true)]
  public void TestUsesPlaceholder(string format, string key, bool expected)
  {
    Assert.AreEqual(expected, FormatStringHelper.UsesPlaceholder(format, key));
  }
}

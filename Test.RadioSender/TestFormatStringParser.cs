using NUnit.Framework;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using System;

namespace Test.RadioSender;

public class TestFormatStringParser
{
  private const string DefaultFormat = "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}";

  [Test]
  public void ParsesDefaultFormat()
  {
    var parser = FormatStringParser.TryCreate(DefaultFormat)!;
    Assert.That(parser, Is.Not.Null);

    var punch = parser.TryParse("1234;31;21:45:59,123", "tcp");

    Assert.That(punch, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(punch!.CompetitorId, Is.EqualTo("1234"));
      Assert.That(punch.Control, Is.EqualTo(31));
      Assert.That(punch.Time.Hour, Is.EqualTo(21));
      Assert.That(punch.Time.Minute, Is.EqualTo(45));
      Assert.That(punch.Time.Second, Is.EqualTo(59));
      Assert.That(punch.Time.Millisecond, Is.EqualTo(123));
      Assert.That(punch.SourceId, Is.EqualTo("tcp"));
    });
  }

  [Test]
  public void ParsedTimeGetsTodaysDateWhenFormatHasNoDate()
  {
    var parser = FormatStringParser.TryCreate(DefaultFormat)!;
    var punch = parser.TryParse("1234;31;21:45:59,123", "tcp");
    var today = DateTime.Now.Date;

    Assert.That(punch!.Time.Date, Is.EqualTo(today));
  }

  [Test]
  public void ParsesFullDateWhenFormatCarriesDate()
  {
    var parser = FormatStringParser.TryCreate("{Card}-{Control}-{Time:yyyy-MM-ddTHH:mm:ss,fff}")!;
    var punch = parser.TryParse("1234-31-2021-08-04T21:45:59,123", "tcp");

    Assert.That(punch, Is.Not.Null);
    Assert.That(punch!.Time, Is.EqualTo(new DateTime(2021, 08, 04, 21, 45, 59, 123)));
  }

  [Test]
  public void CardTokenSetsPunchingCardType()
  {
    var parser = FormatStringParser.TryCreate("{Card};{Control};{Time:HH:mm:ss}")!;
    var punch = parser.TryParse("1234;31;21:45:59", "tcp");

    Assert.That(punch!.CompetitorIdType, Is.EqualTo(CompetitorIdType.PunchingCard));
    Assert.That(punch.CompetitorId, Is.EqualTo("1234"));
  }

  [Test]
  public void BibTokenSetsBibNumberType()
  {
    var parser = FormatStringParser.TryCreate("{Bib};{Control};{Time:HH:mm:ss}")!;
    var punch = parser.TryParse("101;31;21:45:59", "tcp");

    Assert.That(punch!.CompetitorIdType, Is.EqualTo(CompetitorIdType.BibNumber));
    Assert.That(punch.CompetitorId, Is.EqualTo("101"));
  }

  // Round-trip: whatever the Tcp target writes, the source must read back.
  [TestCase("1234", CompetitorIdType.PunchingCard, 31)]
  [TestCase("9", CompetitorIdType.PunchingCard, 250)]
  public void RoundTripsWithFormatStringHelper(string card, CompetitorIdType idType, int control)
  {
    var original = new Punch(
      CompetitorId: card,
      CompetitorIdType: idType,
      Control: control,
      SourceId: "roc",
      ReceivedAt: DateTimeOffset.UtcNow,
      Time: new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 21, 45, 59, 123));

    var line = FormatStringHelper.GetString(original, DefaultFormat).TrimEnd('\r', '\n');
    var parser = FormatStringParser.TryCreate(DefaultFormat)!;
    var parsed = parser.TryParse(line, "tcp");

    Assert.That(parsed, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(parsed!.CompetitorId, Is.EqualTo(original.CompetitorId));
      Assert.That(parsed.Control, Is.EqualTo(original.Control));
      Assert.That(parsed.Time, Is.EqualTo(original.Time));
    });
  }

  // Optional fields the Tcp target renders as "" when absent must still parse:
  // the delimiters disambiguate an empty capture.
  [Test]
  public void ParsesEmptyOptionalFieldBetweenDelimiters()
  {
    var parser = FormatStringParser.TryCreate("{CompetitorId};{Status};{Time:HH:mm:ss,fff}")!;

    // Status Unknown => empty middle field.
    var punch = parser.TryParse("1234;;21:45:59,123", "tcp");

    Assert.That(punch, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(punch!.CompetitorId, Is.EqualTo("1234"));
      Assert.That(punch.CompetitorStatus, Is.EqualTo(CompetitorStatus.Unknown));
      Assert.That(punch.Time.Second, Is.EqualTo(59));
    });
  }

  // Full round-trip through a format carrying several optional fields that are all empty.
  [Test]
  public void RoundTripsWithEmptyOptionalFields()
  {
    const string format = "{CompetitorId};{Type};{Control};{Time:HH:mm:ss,fff};{Status};{Cancellation}{CRLF}";
    var original = new Punch(
      CompetitorId: "1234",
      CompetitorIdType: CompetitorIdType.PunchingCard,
      Control: 31,
      SourceId: "roc",
      ReceivedAt: DateTimeOffset.UtcNow,
      Time: new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 21, 45, 59, 123));
    // ControlType Unknown => {Type}="" ; Status Unknown => {Status}="" ; not cancelled => {Cancellation}=""

    var line = FormatStringHelper.GetString(original, format).TrimEnd('\r', '\n');
    Assert.That(line, Is.EqualTo("1234;;31;21:45:59,123;;")); // sanity: empty optionals

    var parsed = FormatStringParser.TryCreate(format)!.TryParse(line, "tcp");

    Assert.That(parsed, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(parsed!.CompetitorId, Is.EqualTo("1234"));
      Assert.That(parsed.Control, Is.EqualTo(31));
      Assert.That(parsed.Time, Is.EqualTo(original.Time));
      Assert.That(parsed.CompetitorStatus, Is.EqualTo(CompetitorStatus.Unknown));
      Assert.That(parsed.Cancellation, Is.False);
    });
  }

  // The Tcp target encodes DNS..OverTime as 00:00:01..05; the source decodes them.
  [TestCase("21:45:59,000", CompetitorStatus.Unknown)] // real time, not a status
  [TestCase("00:00:01,000", CompetitorStatus.DNS)]
  [TestCase("00:00:02,000", CompetitorStatus.DNF)]
  [TestCase("00:00:03,000", CompetitorStatus.MP)]
  [TestCase("00:00:04,000", CompetitorStatus.DSQ)]
  [TestCase("00:00:05,000", CompetitorStatus.OverTime)]
  public void DecodesSpecialStatusTimes(string time, CompetitorStatus expected)
  {
    var parser = FormatStringParser.TryCreate(DefaultFormat)!;
    var punch = parser.TryParse($"1234;31;{time}", "tcp");

    Assert.That(punch, Is.Not.Null);
    Assert.That(punch!.CompetitorStatus, Is.EqualTo(expected));
  }

  [TestCase("garbage")]
  [TestCase("1234;notanumber;21:45:59,123")]   // non-numeric control
  [TestCase("1234;31;99:99:99,999")]           // impossible time
  [TestCase(";31;21:45:59,123")]               // empty id
  [TestCase("1234;31")]                        // missing time field
  public void ReturnsNullForUnparseableLine(string line)
  {
    var parser = FormatStringParser.TryCreate(DefaultFormat)!;
    var punch = parser.TryParse(line, "tcp");

    Assert.That(punch, Is.Null);
  }

  [Test]
  public void ReturnsNullForFormatWithoutFields()
  {
    Assert.That(FormatStringParser.TryCreate("static text{CRLF}"), Is.Null);
    Assert.That(FormatStringParser.TryCreate(""), Is.Null);
  }
}

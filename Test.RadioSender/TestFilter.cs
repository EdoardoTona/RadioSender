using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;

namespace Test.RadioSender;

public class TestFilter
{
  [Test]
  public void MapsCompetitorIdAndOverridesType()
  {
    var filter = new Filter
    {
      MapCompetitorIds = new() { ["25"] = "250" },
      IncludeOnlyCompetitorIds = new() { "250" },
      OverrideCompetitorIdType = CompetitorIdType.BibNumber
    };

    var punch = CreatePunch("25", CompetitorIdType.PunchingCard);

    var result = filter.Transform(punch);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.CompetitorId, Is.EqualTo("250"));
    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.BibNumber));
  }

  [Test]
  public void MappedCardRetainsItsIdentifierType()
  {
    var filter = new Filter
    {
      MapCompetitorIds = new() { ["25"] = "250" },
      IncludeOnlyCompetitorIds = new() { "250" }
    };

    var punch = CreatePunch("25", CompetitorIdType.PunchingCard);

    var result = filter.Transform(punch);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.CompetitorId, Is.EqualTo("250"));
    Assert.That(result.Card, Is.EqualTo("250"));
    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.PunchingCard));
    Assert.That(punch.CompetitorId, Is.EqualTo("25"));
  }

  private static Punch CreatePunch(string competitorId, CompetitorIdType competitorIdType)
  {
    return new Punch(
      CompetitorId: competitorId,
      CompetitorIdType: competitorIdType,
      Control: 31,
      SourceId: "test",
      ReceivedAt: System.DateTimeOffset.UtcNow,
      Time: new System.DateTime(2021, 08, 04, 21, 45, 59, 123));
  }
}

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
      Name = "test",
      MapCompetitorId = new() { ["25"] = "250" },
      IncludeOnlyCompetitorId = new() { "250" },
      OverrideCompetitorIdType = CompetitorIdType.BibNumber
    };

    var punch = CreatePunch("25", CompetitorIdType.PunchingCard);

    var result = filter.Transform(punch);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.CompetitorId, Is.EqualTo("250"));
    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.BibNumber));
  }

  [Test]
  public void SupportsLegacyCardConfiguration()
  {
    var filter = new Filter
    {
      Name = "test",
      MapCards = new() { ["25"] = "250" },
      IncludeOnlyCards = new() { "250" }
    };

    var punch = CreatePunch("25", CompetitorIdType.PunchingCard);

    var result = filter.Transform(punch);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.CompetitorId, Is.EqualTo("250"));
    Assert.That(result.Card, Is.EqualTo("250"));
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

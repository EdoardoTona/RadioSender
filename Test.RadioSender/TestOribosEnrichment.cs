using System;
using System.Collections.Generic;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Enrichment.Oribos;

namespace Test.RadioSender;

public class TestOribosEnrichment
{
  private static OrServer Server(DateTimeOffset startutc, params OrCompetitor[] competitors)
    => new() { Update = startutc, Race = new OrRace { Startutc = startutc }, Competitors = competitors };

  private static OrServer ServerWithClubs(DateTimeOffset startutc, OrClub[] clubs, params OrCompetitor[] competitors)
    => new() { Update = startutc, Race = new OrRace { Startutc = startutc }, Competitors = competitors, Clubs = clubs };

  private static readonly DateTimeOffset RaceStart = new(2026, 06, 28, 10, 00, 00, TimeSpan.Zero);

  [Test]
  public void CardAndCard2MapToSameBib()
  {
    var data = Server(RaceStart,
      new OrCompetitor { Bib = 101, Card = 1234, Card2 = 5678, Status = "GA" });

    var (cardMap, bibMap, ambiguous) = OribosService.BuildLookups(data);

    Assert.That(ambiguous, Is.EqualTo(0));
    Assert.That(cardMap.ContainsKey("1234"), Is.True);
    Assert.That(cardMap.ContainsKey("5678"), Is.True);
    Assert.That(cardMap["1234"].Bib, Is.EqualTo("101"));
    Assert.That(cardMap["5678"].Bib, Is.EqualTo("101"));
    Assert.That(bibMap["101"].Card, Is.EqualTo("1234"));
    Assert.That(bibMap["101"].Card2, Is.EqualTo("5678")); // both cards exposed on the entry
  }

  [Test]
  public void Entry_PopulatesNameClassNationAndClub()
  {
    var data = ServerWithClubs(RaceStart,
      [new OrClub { CountryId = "7", Country = "ITA", Name = "ASD Foo" }],
      new OrCompetitor
      {
        Bib = 101, Card = 1234, Name = "John", Surname = "Doe", Class = "H21",
        Naz = "GBR", ClubId = "7", ClubCountry = "ITA", Status = "GA"
      });

    var (_, bibMap, _) = OribosService.BuildLookups(data);
    var e = bibMap["101"];

    Assert.That(e.Name, Is.EqualTo("John Doe"));
    Assert.That(e.Class, Is.EqualTo("H21"));
    Assert.That(e.Nation, Is.EqualTo("GBR"));      // athlete nation
    Assert.That(e.ClubId, Is.EqualTo("7"));
    Assert.That(e.ClubName, Is.EqualTo("ASD Foo")); // resolved from Clubs[]
    Assert.That(e.ClubNation, Is.EqualTo("ITA"));   // club nation
  }

  [Test]
  public void AmbiguousCardBothRacing_NotMapped()
  {
    var data = Server(RaceStart,
      new OrCompetitor { Bib = 101, Card = 1234, Status = "GA" },
      new OrCompetitor { Bib = 102, Card = 1234, Status = "IP" });

    var warned = new HashSet<string>();
    var (cardMap, _, ambiguous) = OribosService.BuildLookups(data, warned);

    Assert.That(cardMap.ContainsKey("1234"), Is.False);
    Assert.That(ambiguous, Is.EqualTo(1));
    Assert.That(warned, Does.Contain("card:1234"));
  }

  [Test]
  public void AmbiguousCardOneFinished_MapsToRacingOne()
  {
    var data = Server(RaceStart,
      new OrCompetitor { Bib = 101, Card = 1234, Status = "CL" },  // finished
      new OrCompetitor { Bib = 102, Card = 1234, Status = "GA" }); // still racing

    var (cardMap, _, ambiguous) = OribosService.BuildLookups(data);

    Assert.That(ambiguous, Is.EqualTo(0));
    Assert.That(cardMap["1234"].Bib, Is.EqualTo("102"));
  }

  [TestCase("PM", CompetitorStatus.MP)]
  [TestCase("PE", CompetitorStatus.MP)]
  [TestCase("NP", CompetitorStatus.DNS)]
  [TestCase("SQ", CompetitorStatus.DSQ)]
  [TestCase("RI", CompetitorStatus.DNF)]
  [TestCase("FT", CompetitorStatus.OverTime)]
  [TestCase("CL", CompetitorStatus.OK)]
  [TestCase("GA", CompetitorStatus.Running)]
  [TestCase("IP", CompetitorStatus.WaitingStart)]
  public void MapStatus_Known(string oribos, CompetitorStatus expected)
    => Assert.That(OribosService.MapStatus(oribos), Is.EqualTo(expected));

  [TestCase("DI")]
  [TestCase("ZZ")]
  [TestCase("")]
  [TestCase(null)]
  public void MapStatus_Unknown_ReturnsNull(string? oribos)
    => Assert.That(OribosService.MapStatus(oribos), Is.Null);

  [Test]
  public void NormalizeRelativeStart_Normal()
    => Assert.That(OribosService.NormalizeRelativeStart(3600), Is.EqualTo(3600));

  [Test]
  public void NormalizeRelativeStart_BeforeRaceStart_WrapsMinus12h()
    => Assert.That(OribosService.NormalizeRelativeStart(3600 * 11.5), Is.EqualTo(3600 * 11.5 - 3600 * 12));

  [Test]
  public void AbsoluteStart_AddsRelativeSeconds_Local()
  {
    var abs = OribosService.AbsoluteStart(RaceStart, 1800); // +30min
    Assert.That(abs, Is.EqualTo((RaceStart + TimeSpan.FromMinutes(30)).LocalDateTime));
  }

  [Test]
  public void AbsoluteStart_ZeroOrNegative_IsNull()
  {
    Assert.That(OribosService.AbsoluteStart(RaceStart, 0), Is.Null);
    Assert.That(OribosService.AbsoluteStart(RaceStart, -5), Is.Null);
  }
}

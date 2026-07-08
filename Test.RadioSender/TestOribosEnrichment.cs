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

  [Test]
  public void AbsoluteFinish_AddsRelativeSeconds_Local()
    => Assert.That(OribosService.AbsoluteFinish(RaceStart, 5741),
                   Is.EqualTo((RaceStart + TimeSpan.FromSeconds(5741)).LocalDateTime));

  [Test]
  public void AbsoluteFinish_ZeroOrNegative_IsNull()
  {
    Assert.That(OribosService.AbsoluteFinish(RaceStart, 0), Is.Null);
    Assert.That(OribosService.AbsoluteFinish(RaceStart, -5), Is.Null);
  }

  [Test]
  public void Entry_PopulatesFinishTimeAndSubJudice()
  {
    var data = Server(RaceStart,
      new OrCompetitor { Bib = 101, Status = "CL", Finish = 3600, Sj = true });

    var (_, bibMap, _) = OribosService.BuildLookups(data);

    Assert.That(bibMap["101"].FinishTime, Is.EqualTo((RaceStart + TimeSpan.FromHours(1)).LocalDateTime));
    Assert.That(bibMap["101"].SubJudice, Is.True);
  }

  // --- enrichment / id type resolution ---

  private static Punch Punch(string competitorId, CompetitorIdType idType) => new(
    CompetitorId: competitorId,
    CompetitorIdType: idType,
    Control: 31,
    SourceId: "test",
    ReceivedAt: DateTimeOffset.UtcNow,
    Time: new DateTime(2026, 06, 28, 10, 30, 0));

  private static (IReadOnlyDictionary<string, OribosEntry> cardMap, IReadOnlyDictionary<string, OribosEntry> bibMap) Maps(params OrCompetitor[] competitors)
  {
    var (cardMap, bibMap, _) = OribosService.BuildLookups(Server(RaceStart, competitors));
    return (cardMap, bibMap);
  }

  [Test]
  public void Enrich_UnknownIdType_CardHit_SetsPunchingCardAndCompetitor()
  {
    var (cardMap, bibMap) = Maps(new OrCompetitor { Bib = 101, Card = 1234, Name = "John", Surname = "Doe", Status = "GA" });

    var result = OribosService.Enrich(Punch("1234", CompetitorIdType.Unknown), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.PunchingCard));
    Assert.That(result.CompetitorId, Is.EqualTo("1234")); // id itself is untouched
    Assert.That(result.Competitor?.Bib, Is.EqualTo("101"));
    Assert.That(result.Competitor?.Name, Is.EqualTo("John Doe"));
  }

  [Test]
  public void Enrich_UnknownIdType_BibHitOnly_SetsBibNumber()
  {
    // bib 101 with no card assigned: an unknown id "101" resolves only via the bib map
    var (cardMap, bibMap) = Maps(new OrCompetitor { Bib = 101, Name = "John", Surname = "Doe", Status = "GA" });

    var result = OribosService.Enrich(Punch("101", CompetitorIdType.Unknown), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.BibNumber));
    Assert.That(result.Competitor?.Bib, Is.EqualTo("101"));
  }

  [Test]
  public void Enrich_UnknownIdType_CardHitWinsOverBib()
  {
    // "1234" is competitor A's card AND competitor B's bib: card map is tried first
    var (cardMap, bibMap) = Maps(
      new OrCompetitor { Bib = 1234, Name = "Bibby", Status = "GA" },
      new OrCompetitor { Bib = 55, Card = 1234, Name = "Carrie", Status = "GA" });

    var result = OribosService.Enrich(Punch("1234", CompetitorIdType.Unknown), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.PunchingCard));
    Assert.That(result.Competitor?.Bib, Is.EqualTo("55")); // resolved via the card
  }

  [Test]
  public void Enrich_UnknownIdType_NoMatch_PassesThroughUnchanged()
  {
    var (cardMap, bibMap) = Maps(new OrCompetitor { Bib = 101, Card = 1234, Status = "GA" });

    var result = OribosService.Enrich(Punch("9999", CompetitorIdType.Unknown), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.Unknown)); // stays unknown
    Assert.That(result.Competitor, Is.Null);
  }

  [Test]
  public void Enrich_KnownCardType_KeepsTypeAndEnriches()
  {
    var (cardMap, bibMap) = Maps(new OrCompetitor { Bib = 101, Card = 1234, Status = "GA" });

    var result = OribosService.Enrich(Punch("1234", CompetitorIdType.PunchingCard), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.PunchingCard));
    Assert.That(result.Competitor?.Bib, Is.EqualTo("101"));
  }

  [Test]
  public void Enrich_KnownBibType_DoesNotFallBackToCardMap()
  {
    // id "1234" exists as a card but the punch says it's a bib: no bib match → no enrichment,
    // and the type is not silently rewritten to PunchingCard
    var (cardMap, bibMap) = Maps(new OrCompetitor { Bib = 101, Card = 1234, Status = "GA" });

    var result = OribosService.Enrich(Punch("1234", CompetitorIdType.BibNumber), cardMap, bibMap);

    Assert.That(result.CompetitorIdType, Is.EqualTo(CompetitorIdType.BibNumber));
    Assert.That(result.Competitor, Is.Null);
  }

  // --- transition evaluation ---

  [TestCase("GA", "PM", CompetitorStatus.MP)]
  [TestCase("CL", "SQ", CompetitorStatus.DSQ)]
  [TestCase(null, "PM", CompetitorStatus.MP)] // unknown prev: anomalous always emitted
  public void EvaluateTransition_ToAnomalous_Emitted(string? prev, string next, CompetitorStatus expected)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.EqualTo((expected, false)));

  [TestCase("PM", "CL")]
  [TestCase("PE", "CL")]
  [TestCase("SQ", "CL")]
  [TestCase("RI", "CL")]
  [TestCase("FT", "CL")]
  [TestCase("NP", "CL")]
  public void EvaluateTransition_AnomalousToClassified_EmitsOkWithFinishTime(string prev, string next)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.EqualTo((CompetitorStatus.OK, true)));

  [TestCase("GA", "CL")] // normal arrival: the time already flows through regular punches
  [TestCase("IP", "CL")]
  public void EvaluateTransition_RegularToClassified_NotEmitted(string? prev, string next)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.Null);

  [TestCase(null)] // bib first seen sub judice (e.g. service started during a review)
  [TestCase("DI")] // unmapped previous status
  public void EvaluateTransition_UnknownPrevToClassified_EmitsOkWithFinishTime(string? prev)
    => Assert.That(OribosService.EvaluateTransition(prev, "CL"), Is.EqualTo((CompetitorStatus.OK, true)));

  [TestCase("CL", "GA", CompetitorStatus.Running)]
  [TestCase("CL", "IP", CompetitorStatus.WaitingStart)]
  [TestCase("PM", "GA", CompetitorStatus.Running)]
  [TestCase("SQ", "IP", CompetitorStatus.WaitingStart)]
  public void EvaluateTransition_FinalOutcomeReset_Emitted(string prev, string next, CompetitorStatus expected)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.EqualTo((expected, false)));

  [TestCase("IP", "GA")] // normal pre-arrival progression
  [TestCase("GA", "IP")]
  [TestCase(null, "GA")]
  [TestCase(null, "IP")]
  public void EvaluateTransition_RegularProgression_NotEmitted(string? prev, string next)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.Null);

  [TestCase("PM", "DI")]
  [TestCase("CL", "")]
  public void EvaluateTransition_UnmappedNewStatus_NotEmitted(string prev, string next)
    => Assert.That(OribosService.EvaluateTransition(prev, next), Is.Null);

  // --- snapshot / sub judice ---

  private static OrCompetitor Comp(int bib, string status, bool sj = false, double finish = 0)
    => new() { Bib = bib, Status = status, Sj = sj, Finish = finish };

  private static IReadOnlyDictionary<string, OribosEntry> BibMap(params OrCompetitor[] competitors)
    => OribosService.BuildLookups(Server(RaceStart, competitors)).bibMap;

  [Test]
  public void ComputeStatusChanges_FirstFetch_InitializesWithoutEmitting()
  {
    var (snapshot, toEmit) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "PM")), new Dictionary<string, string>(), initialized: false);

    Assert.That(toEmit, Is.Empty);
    Assert.That(snapshot["101"], Is.EqualTo("PM"));
  }

  [Test]
  public void ComputeStatusChanges_SubJudice_FreezesStatusAndEmitsNothing()
  {
    // GA → CL+sj (arrival waiting for punch check): nothing emitted, snapshot keeps GA
    var prev = new Dictionary<string, string> { ["101"] = "GA" };

    var (snapshot, toEmit) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "CL", sj: true, finish: 3600)), prev, initialized: true);

    Assert.That(toEmit, Is.Empty);
    Assert.That(snapshot["101"], Is.EqualTo("GA"));
  }

  [Test]
  public void ComputeStatusChanges_SubJudiceCleared_EmitsAgainstConfirmedStatus()
  {
    // GA (frozen through the sj fetches) → PM confirmed: MP emitted
    var prev = new Dictionary<string, string> { ["101"] = "GA" };

    var (snapshot, toEmit) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "PM")), prev, initialized: true);

    Assert.That(toEmit, Has.Count.EqualTo(1));
    Assert.That(toEmit[0].status, Is.EqualTo(CompetitorStatus.MP));
    Assert.That(snapshot["101"], Is.EqualTo("PM"));
  }

  [Test]
  public void ComputeStatusChanges_SubJudiceOnFirstFetch_NotSnapshotted_EmitsOnceConfirmed()
  {
    // restart during a review: the confirmed status must still reach the targets
    var (s1, e1) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "CL", sj: true, finish: 3600)), new Dictionary<string, string>(), initialized: false);

    Assert.That(e1, Is.Empty);
    Assert.That(s1.ContainsKey("101"), Is.False);

    var (_, e2) = OribosService.ComputeStatusChanges(BibMap(Comp(101, "PM")), s1, initialized: true);

    Assert.That(e2, Has.Count.EqualTo(1));
    Assert.That(e2[0].status, Is.EqualTo(CompetitorStatus.MP));
  }

  [Test]
  public void ComputeStatusChanges_AnomalousToClassified_EmitsOkWithFinishTime()
  {
    var prev = new Dictionary<string, string> { ["101"] = "PM" };

    var (snapshot, toEmit) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "CL", finish: 3600)), prev, initialized: true);

    Assert.That(toEmit, Has.Count.EqualTo(1));
    Assert.That(toEmit[0].status, Is.EqualTo(CompetitorStatus.OK));
    Assert.That(toEmit[0].useFinishTime, Is.True);
    Assert.That(toEmit[0].entry.FinishTime, Is.EqualTo((RaceStart + TimeSpan.FromHours(1)).LocalDateTime));
    Assert.That(snapshot["101"], Is.EqualTo("CL"));
  }

  [Test]
  public void ComputeStatusChanges_ClassifiedWithoutFinishTime_KeptPendingNotEmitted()
  {
    var prev = new Dictionary<string, string> { ["101"] = "PM" };

    var (snapshot, toEmit) = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "CL")), prev, initialized: true);

    Assert.That(toEmit, Is.Empty);
    Assert.That(snapshot["101"], Is.EqualTo("PM")); // retried when the finish time appears
  }

  [Test]
  public void ComputeStatusChanges_Sequence_PmThenSjThenClConfirmed_EmitsOk()
  {
    // PM → sj on → CL (still sj) → sj off: MP emitted first, then OK with finish time
    var f0 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "GA")), new Dictionary<string, string>(), initialized: false);
    var f1 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "PM", finish: 3600)), f0.snapshot, initialized: true);
    var f2 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "PM", sj: true, finish: 3600)), f1.snapshot, initialized: true);
    var f3 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "CL", sj: true, finish: 3600)), f2.snapshot, initialized: true);
    var f4 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "CL", finish: 3600)), f3.snapshot, initialized: true);

    Assert.That(f1.toEmit[0].status, Is.EqualTo(CompetitorStatus.MP));
    Assert.That(f2.toEmit, Is.Empty);
    Assert.That(f3.toEmit, Is.Empty);
    Assert.That(f4.toEmit, Has.Count.EqualTo(1));
    Assert.That(f4.toEmit[0].status, Is.EqualTo(CompetitorStatus.OK));
    Assert.That(f4.toEmit[0].useFinishTime, Is.True);
  }

  [Test]
  public void ComputeStatusChanges_Sequence_RestartDuringSj_ClConfirmed_EmitsOk()
  {
    // service (re)starts while the bib is already PM+sj: the pre-sj status is unknown,
    // but the confirmed classification must still reach the targets
    var f0 = OribosService.ComputeStatusChanges(
      BibMap(Comp(101, "PM", sj: true, finish: 3600)), new Dictionary<string, string>(), initialized: false);
    var f1 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "CL", finish: 3600)), f0.snapshot, initialized: true);

    Assert.That(f0.toEmit, Is.Empty);
    Assert.That(f1.toEmit, Has.Count.EqualTo(1));
    Assert.That(f1.toEmit[0].status, Is.EqualTo(CompetitorStatus.OK));
    Assert.That(f1.toEmit[0].useFinishTime, Is.True);
  }

  [Test]
  public void ComputeStatusChanges_Sequence_ClThenSjThenPmConfirmed_EmitsMp()
  {
    // CL → sj on → PM (still sj) → sj off: only the confirmed MP is emitted
    var f0 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "CL", finish: 3600)), new Dictionary<string, string>(), initialized: false);
    var f1 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "CL", sj: true, finish: 3600)), f0.snapshot, initialized: true);
    var f2 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "PM", sj: true, finish: 3600)), f1.snapshot, initialized: true);
    var f3 = OribosService.ComputeStatusChanges(BibMap(Comp(101, "PM", finish: 3600)), f2.snapshot, initialized: true);

    Assert.That(f1.toEmit, Is.Empty);
    Assert.That(f2.toEmit, Is.Empty);
    Assert.That(f3.toEmit, Has.Count.EqualTo(1));
    Assert.That(f3.toEmit[0].status, Is.EqualTo(CompetitorStatus.MP));
  }

  [Test]
  public void OrCompetitor_DeserializesFinishAndSj_FromFullwebJson()
  {
    // fragment taken from a real ORServer.fullweb.jsp response
    const string json = """{"bib":31,"card":8190498,"status":"CL","start":4710,"finish":5771.3,"sj":true}""";
    var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    var c = System.Text.Json.JsonSerializer.Deserialize<OrCompetitor>(json, options)!;

    Assert.That(c.Finish, Is.EqualTo(5771.3));
    Assert.That(c.Sj, Is.True);
    Assert.That(c.Status, Is.EqualTo("CL"));
  }
}

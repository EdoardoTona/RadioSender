using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Enrichment;

namespace Test.RadioSender;

public class TestFilterServiceEnrichment
{
  // sets Competitor.Name to a fixed value (and optionally Bib) to make ordering observable
  private sealed class FakeEnricher(string name, string writeName, string? writeBib = null) : IEnrichmentSource
  {
    public string Name => name;
    public Punch Enrich(Punch punch) => punch with
    {
      Competitor = new Competitor(
        Bib: writeBib ?? punch.Competitor?.Bib,
        Card: punch.Competitor?.Card,
        Name: writeName)
    };
  }

  private sealed class StubMonitor(FiltersConfiguration value) : IOptionsMonitor<FiltersConfiguration>
  {
    public FiltersConfiguration CurrentValue => value;
    public FiltersConfiguration Get(string? name) => value;
    public IDisposable? OnChange(Action<FiltersConfiguration, string?> listener) => null;
  }

  private static FilterService BuildService(Filter filter, params IEnrichmentSource[] enrichers)
    => new(new StubMonitor(new FiltersConfiguration { List = [filter] }), enrichers);

  private static Punch CardPunch() => new(
    CompetitorId: "1234",
    CompetitorIdType: CompetitorIdType.PunchingCard,
    Control: 31,
    SourceId: "test",
    ReceivedAt: DateTimeOffset.UtcNow,
    Time: new DateTime(2026, 06, 28, 10, 30, 0));

  [Test]
  public void Enricher_CardToBib_Applied()
  {
    var filter = new Filter { Name = "f", Enrichers = ["A"] };
    var svc = BuildService(filter, new FakeEnricher("A", "Alice", writeBib: "101"));

    var result = svc.Transform("f", CardPunch());

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Competitor?.Bib, Is.EqualTo("101"));
    Assert.That(result.Competitor?.Name, Is.EqualTo("Alice"));
  }

  [Test]
  public void Enrichers_OrderRespected_LastWins()
  {
    var filter = new Filter { Name = "f", Enrichers = ["A", "B"] };
    var svc = BuildService(filter,
      new FakeEnricher("A", "Alice"),
      new FakeEnricher("B", "Bob"));

    var result = svc.Transform("f", CardPunch());

    // both write Name; B is listed last so it wins
    Assert.That(result!.Competitor?.Name, Is.EqualTo("Bob"));
  }

  [Test]
  public void Enrichers_OrderRespected_ReversedList()
  {
    var filter = new Filter { Name = "f", Enrichers = ["B", "A"] };
    var svc = BuildService(filter,
      new FakeEnricher("A", "Alice"),
      new FakeEnricher("B", "Bob"));

    var result = svc.Transform("f", CardPunch());

    Assert.That(result!.Competitor?.Name, Is.EqualTo("Alice"));
  }

  [Test]
  public void NoEnrichers_PunchUnchanged()
  {
    var filter = new Filter { Name = "f" }; // Enrichers empty
    var svc = BuildService(filter, new FakeEnricher("A", "Alice"));

    var result = svc.Transform("f", CardPunch());

    Assert.That(result!.Competitor, Is.Null);
  }

  [Test]
  public void UnknownEnricherName_Ignored()
  {
    var filter = new Filter { Name = "f", Enrichers = ["does-not-exist"] };
    var svc = BuildService(filter, new FakeEnricher("A", "Alice"));

    var result = svc.Transform("f", CardPunch());

    Assert.That(result!.Competitor, Is.Null);
  }
}

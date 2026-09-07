using NUnit.Framework;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Enrichment;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Test.RadioSender;

public class TestEnrichmentModule
{
  private sealed class Provider(string name, string value, string? bib = null) : FlowModule, IEnrichmentSource
  {
    public string Name => name;
    public Punch Enrich(Punch punch) => punch with { Competitor = new(Bib: bib ?? punch.Competitor?.Bib, Name: value) };
  }
  private static Punch CardPunch() => new("1234", new(2026, 6, 28, 10, 30, 0), 31, "test", DateTimeOffset.UtcNow, CompetitorIdType.PunchingCard);
  private static EnrichmentModule Module(string id, params Provider[] providers) => new(new() { ProviderId = id },
    new("enrichment", Path.GetTempPath(), new CapturingOutput(), key => providers.FirstOrDefault(p => p.Name == key)));

  [Test]
  public async Task Enrichment_AttachesCompetitorAndKeepsOriginalPunch()
  {
    await using var provider = new Provider("race", "Alex", "101");
    await using var module = Module("race", provider);
    var original = CardPunch(); var result = module.Process(original, false);
    Assert.That(result?.Competitor?.Bib, Is.EqualTo("101"));
    Assert.That(result?.Competitor?.Name, Is.EqualTo("Alex"));
    Assert.That(original.Competitor, Is.Null);
  }
  [TestCase(false, "Bob")]
  [TestCase(true, "Alice")]
  public async Task ChainedEnrichment_LastProcessorWins(bool reverse, string expected)
  {
    await using var a = new Provider("a", "Alice", "101");
    await using var b = new Provider("b", "Bob");
    await using var first = Module(reverse ? "b" : "a", a, b);
    await using var second = Module(reverse ? "a" : "b", a, b);
    var result = second.Process(first.Process(CardPunch(), false)!, false);
    Assert.That(result?.Competitor?.Name, Is.EqualTo(expected));
    Assert.That(result?.Competitor?.Bib, Is.EqualTo("101"));
  }
  [Test]
  public async Task Replay_UsesCurrentProviderData()
  {
    await using var provider = new Provider("race", "Current", "101");
    await using var module = Module("race", provider);
    Assert.That(module.Process(CardPunch() with { Competitor = new(Name: "Historical") }, true)?.Competitor?.Name, Is.EqualTo("Current"));
  }
  [Test]
  public async Task MissingProvider_IsAnExplicitError()
  {
    await using var module = Module("missing");
    Assert.Throws<FlowException>(() => module.Process(CardPunch(), false));
  }
}

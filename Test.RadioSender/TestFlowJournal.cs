using NUnit.Framework;
using RadioSender.Flow;
using RadioSender.Hosts.Common;
using RadioSender.Runtime;
using System;
using System.Linq;

namespace Test.RadioSender;

[TestFixture]
public class TestFlowJournal
{
  [Test]
  public void ExpiredAndWrongPortSelections_AreRejected_AndDeltaRemainsMonotonic()
  {
    var journal = new FlowJournal();
    var punch = new Punch("123", DateTime.Now, 35, "source", DateTimeOffset.UtcNow);
    var first = journal.Add(Guid.NewGuid(), Guid.NewGuid(), null, "source", "output", null, 1, punch);
    for (var i = 0; i < 600; i++) journal.Add(Guid.NewGuid(), Guid.NewGuid(), null, "source", "output", null, 1, punch);
    var page = journal.Read("source", "output", first.Id);
    Assert.That(page.Items, Has.Count.EqualTo(500));
    Assert.That(page.HistoryExpired, Is.True);
    Assert.That(page.Evicted, Is.EqualTo(101));
    Assert.Throws<FlowException>(() => journal.Resolve("source", "output", [first.Id]));
    Assert.Throws<FlowException>(() => journal.Resolve("source", "input", [page.Items.Last().Id]));
    Assert.That(journal.Read("source", "output", page.Cursor).Items, Is.Empty);
    journal.Clear();
    var next = journal.Add(Guid.NewGuid(), Guid.NewGuid(), null, "source", "output", null, 2, punch);
    Assert.That(next.Id, Is.GreaterThan(page.Cursor));
  }
}

using NUnit.Framework;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Test.RadioSender;

public class TestDeduplicateModule
{
  [TestCase(new[] { false, false }, new[] { false })]
  [TestCase(new[] { false, true, false }, new[] { false, true, false })]
  [TestCase(new[] { false, true, true }, new[] { false, true })]
  public async Task RepeatedAndCancelledPunches_FollowLiveState(bool[] input, bool[] expected)
  {
    await using var module = new DeduplicateModule(new());
    var punch = new Punch("7", new(2026, 7, 24, 10, 1, 3), 90, "test", DateTimeOffset.UtcNow);
    var output = new List<Punch>();
    foreach (var cancelled in input)
      if (module.Process(punch with { Cancellation = cancelled }, false) is { } forwarded) output.Add(forwarded);
    Assert.That(output.Select(p => p.Cancellation), Is.EqualTo(expected));
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;

namespace Test.RadioSender;

public class TestDispatcherServiceDedup
{
  private sealed class StubMonitor(FiltersConfiguration value) : IOptionsMonitor<FiltersConfiguration>
  {
    public FiltersConfiguration CurrentValue => value;
    public FiltersConfiguration Get(string? name) => value;
    public IDisposable? OnChange(Action<FiltersConfiguration, string?> listener) => null;
  }

  private sealed class RecordingTarget : ITarget
  {
    public List<Punch> Received { get; } = [];

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches != null)
        Received.AddRange(dispatch.Punches);
      return Task.CompletedTask;
    }

    public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var d in dispatches)
        SendDispatch(d, ct);
      return Task.CompletedTask;
    }
  }

  private static (DispatcherService svc, RecordingTarget target) BuildService()
  {
    var filterService = new FilterService(new StubMonitor(new FiltersConfiguration { List = [new Filter { Name = "f" }] }), []);
    var target = new RecordingTarget();
    var svc = new DispatcherService(filterService, [target], new DispatcherConfiguration());
    return (svc, target);
  }

  private static Punch Punch(string competitorId, DateTime time, bool cancellation = false, CompetitorStatus status = CompetitorStatus.Unknown) => new(
    CompetitorId: competitorId,
    Control: 90,
    SourceId: "test",
    ReceivedAt: DateTimeOffset.UtcNow,
    Time: time,
    Cancellation: cancellation,
    CompetitorStatus: status);

  [Test]
  public async Task IdenticalPunch_SentTwice_SecondIsDropped()
  {
    var (svc, target) = BuildService();
    var t = new DateTime(2026, 07, 24, 10, 1, 3);

    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t)]));
    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t)]));
    await Task.Delay(50);

    Assert.That(target.Received.Count, Is.EqualTo(1));
  }

  [Test]
  public async Task RestoreAfterCancellation_IsForwardedAgain()
  {
    // bib 7 @ T1, then cancelled, then restored (re-sent identical to the original) — the
    // restore must reach the target instead of being dropped as a duplicate of the original.
    var (svc, target) = BuildService();
    var t1 = new DateTime(2026, 07, 24, 10, 1, 3);

    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1)]));                         // original
    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1, cancellation: true)]));      // cancellation
    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1)]));                          // restore
    await Task.Delay(50);

    Assert.That(target.Received.Count, Is.EqualTo(3));
    Assert.That(target.Received.Count(p => p.CompetitorId == "7" && !p.Cancellation), Is.EqualTo(2));
    Assert.That(target.Received.Count(p => p.CompetitorId == "7" && p.Cancellation), Is.EqualTo(1));
  }

  [Test]
  public async Task CancellationSentTwice_SecondCancellationIsStillDeduplicated()
  {
    // the cancellation itself must remain subject to normal dedup (only the punch it
    // cancels becomes re-sendable, not the cancellation event itself).
    var (svc, target) = BuildService();
    var t1 = new DateTime(2026, 07, 24, 10, 1, 3);

    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1)]));
    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1, cancellation: true)]));
    svc.PushDispatch(new PunchDispatch(Punches: [Punch("7", t1, cancellation: true)]));
    await Task.Delay(50);

    Assert.That(target.Received.Count, Is.EqualTo(2));
  }
}

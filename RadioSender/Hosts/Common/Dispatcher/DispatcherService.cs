using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Target;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common;

public record ReplayResult(int Punches, int Targets);

public sealed class DispatcherService(
  FilterService filterService,
  IEnumerable<ITarget> targets,
  DispatcherConfiguration configuration
    ) : IDisposable
{
  private readonly ConcurrentDictionary<string, Punch> _punches = [];
  private readonly List<string> _punchOrder = [];
  private readonly object _punchOrderLock = new();
  private readonly IReadOnlyList<ITarget> _targets = targets.ToList();

  public event EventHandler? RequestPing;
  private readonly List<IDisposable> _subscriptions = [];

  public void ResendPunches()
  {
    _ = ReplayLatestPunchesAsync(null);
  }

  public Task<ReplayResult> ReplayPunchAsync(string comparisonKey, CancellationToken ct = default)
  {
    if (!_punches.TryGetValue(comparisonKey, out var punch))
    {
      Log.Warning("Replay requested for missing punch {key}", comparisonKey);
      return Task.FromResult(new ReplayResult(0, 0));
    }

    return ReplayPunchesAsync([punch], ct);
  }

  public Task<ReplayResult> ReplayLatestPunchesAsync(int? count, CancellationToken ct = default)
  {
    var punches = GetLatestPunches(count);
    return ReplayPunchesAsync(punches, ct);
  }

  public void Ping()
  {
    RequestPing?.Invoke(this, EventArgs.Empty);
  }

  public void PushDispatch(PunchDispatch dispatch)
  {
    if (dispatch.Punches != null)
    {
      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);
      var toBeForwardedPunch = DeduplicateAndStore(punches);

      dispatch = dispatch with { Punches = toBeForwardedPunch };
    }

    _ = Task.WhenAll(_targets.Select(t => t.SendDispatch(dispatch, default)));
  }

  public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
  {
    var toBeForwardedDispatcher = new List<PunchDispatch>();
    foreach (var dispatch in dispatches)
    {
      if (dispatch.Punches == null)
        continue;

      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);
      var toBeForwardedPunch = DeduplicateAndStore(punches);

      toBeForwardedDispatcher.Add(dispatch with { Punches = toBeForwardedPunch });
    }

    _ = Task.WhenAll(_targets.Select(t => t.SendDispatches(toBeForwardedDispatcher, default)));
  }

  private List<Punch> DeduplicateAndStore(IEnumerable<Punch> punches)
  {
    var toBeForwardedPunch = new List<Punch>();
    foreach (var punch in punches)
    {
      var key = punch.ComparisonKey;
      if (!_punches.TryAdd(key, punch))
      {
        Log.Verbose("Detected duplicated punch {key}", key);
        continue;
      }

      lock (_punchOrderLock)
      {
        _punchOrder.Add(key);
      }

      toBeForwardedPunch.Add(punch);
    }

    return toBeForwardedPunch;
  }

  private IReadOnlyList<Punch> GetLatestPunches(int? count)
  {
    if (count is <= 0)
      return [];

    List<string> keys;
    lock (_punchOrderLock)
    {
      var skip = count is > 0 && count.Value < _punchOrder.Count
        ? _punchOrder.Count - count.Value
        : 0;

      keys = _punchOrder.Skip(skip).ToList();
    }

    return keys
      .Select(key => _punches.TryGetValue(key, out var punch) ? punch : null)
      .Where(punch => punch != null)
      .Select(punch => punch!)
      .ToList();
  }

  private async Task<ReplayResult> ReplayPunchesAsync(IReadOnlyList<Punch> punches, CancellationToken ct)
  {
    var replayTargets = _targets.Where(t => t.Descriptor.ManualReplay).ToList();

    if (punches.Count == 0 || replayTargets.Count == 0)
      return new ReplayResult(punches.Count, replayTargets.Count);

    await Task.WhenAll(replayTargets.Select(t => t.SendDispatch(new PunchDispatch(punches), ct)));

    Log.Information("Replayed {punchCount} punches to {targetCount} targets", punches.Count, replayTargets.Count);

    return new ReplayResult(punches.Count, replayTargets.Count);
  }

  public void Dispose()
  {
    foreach (var item in _subscriptions)
      item.Dispose();
  }
}

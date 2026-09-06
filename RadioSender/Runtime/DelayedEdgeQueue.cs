using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RadioSender.Runtime;

internal sealed class DelayedEdgeQueue(int delayMs, Func<FlowEvent, Task> deliver, Action<FlowEvent, string> reject) : IAsyncDisposable
{
  private readonly Channel<(FlowEvent Event, long AcceptedAt)> _queue = Channel.CreateBounded<(FlowEvent, long)>(
    new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
  private readonly CancellationTokenSource _lifetime = new();
  private readonly object _sync = new();
  private TaskCompletionSource _idle = Completed();
  private int _pending;
  private Task? _worker;
  private int _disposed;
  public int Pending => Volatile.Read(ref _pending);
  private static TaskCompletionSource Completed() { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }
  public void Start() => _worker = RunAsync();

  public async Task EnqueueAsync(FlowEvent item)
  {
    lock (_sync) { if (_pending++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    try { await _queue.Writer.WriteAsync((item, Stopwatch.GetTimestamp()), timeout.Token); }
    catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
    { reject(item, "The delayed edge queue is full or closed."); Complete(); }
  }
  private async Task RunAsync()
  {
    await foreach (var work in _queue.Reader.ReadAllAsync())
    {
      try
      {
        var remaining = TimeSpan.FromMilliseconds(delayMs) - Stopwatch.GetElapsedTime(work.AcceptedAt);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        await deliver(work.Event);
      }
      catch (OperationCanceledException) { reject(work.Event, "The delayed event was cancelled when the flow stopped."); }
      catch (Exception e) { reject(work.Event, e.Message); }
      finally { Complete(); }
    }
  }
  private void Complete() { lock (_sync) { if (--_pending == 0) _idle.TrySetResult(); } }
  public Task DrainAsync(CancellationToken ct) { lock (_sync) return _idle.Task.WaitAsync(ct); }
  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _queue.Writer.TryComplete();
    await _lifetime.CancelAsync();
    if (_worker != null) await _worker;
    _lifetime.Dispose();
  }
}

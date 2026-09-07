using RadioSender.Flow.Modules;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RadioSender.Runtime;

internal sealed class TargetQueue(FlowModule module, Action<FlowEvent, string, long, DeliveryResult> observe) : IAsyncDisposable
{
  private readonly Channel<(FlowEvent Event, string EdgeId, long Revision)> _queue =
    Channel.CreateBounded<(FlowEvent, string, long)>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
  private readonly object _sync = new();
  private TaskCompletionSource _idle = Completed();
  private int _pending;
  private Task? _worker;
  private readonly CancellationTokenSource _lifetime = new();
  private int _disposed;
  public int Pending => Volatile.Read(ref _pending);
  private static TaskCompletionSource Completed() { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }

  public void Start() => _worker = RunAsync();
  public async Task EnqueueAsync(FlowEvent item, string edgeId, long revision)
  {
    lock (_sync)
    { if (_pending++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    try { await _queue.Writer.WriteAsync((item, edgeId, revision), timeout.Token); }
    catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
    { observe(item, edgeId, revision, new("Rejected", "The target queue is full or closed. Retry from the inspector.")); Complete(); }
  }

  private async Task RunAsync()
  {
    await foreach (var work in _queue.Reader.ReadAllAsync())
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
      timeout.CancelAfter(TimeSpan.FromSeconds(5));
      try
      {
        _lifetime.Token.ThrowIfCancellationRequested();
        var result = await module.SendAsync(work.Event.Punch, timeout.Token);
        observe(work.Event, work.EdgeId, work.Revision, result);
      }
      catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
      { observe(work.Event, work.EdgeId, work.Revision, new("Rejected", "Delivery cancelled when the flow stopped. The receiver may have received a partial message.")); }
      catch (OperationCanceledException) when (timeout.IsCancellationRequested)
      { observe(work.Event, work.EdgeId, work.Revision, new("Failed", "Delivery exceeded the five-second deadline. The receiver may have received the event; check before retrying.")); }
      catch (Exception e) { observe(work.Event, work.EdgeId, work.Revision, new("Failed", e.Message)); }
      finally { Complete(); }
    }
  }
  private void Complete()
  {
    lock (_sync) { if (--_pending == 0) _idle.TrySetResult(); }
  }
  public Task DrainAsync(CancellationToken ct)
  { lock (_sync) return _idle.Task.WaitAsync(ct); }
  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _queue.Writer.TryComplete();
    await _lifetime.CancelAsync();
    if (_worker != null) await _worker;
    _lifetime.Dispose();
  }
}

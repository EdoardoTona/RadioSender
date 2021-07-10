using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Helpers
{
  public abstract class TimedHostedService : IHostedService, IDisposable
  {
    protected abstract TimeSpan InitialDelay { get; }
    protected abstract TimeSpan TimerInterval { get; }
    /// <summary>
    /// Default false
    /// </summary>
    protected bool CallbacksCanOverlap { get; set; } = false;
    protected CancellationToken CancellationToken { get { return _cts?.Token ?? default; } }

    private CancellationTokenSource _cts;
    private Timer _timer;
    private readonly ConcurrentBag<Task> _onTickTasks = new();
    private Task _onStartTask;
    private Task _onStopTask;
    private readonly object _timerLock = new();

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="startingToken">Indicates that the start process has been aborted.</param>
    protected virtual Task OnStart(CancellationToken startingToken) { return Task.CompletedTask; }
    /// <summary>
    /// Action must be executed every tick
    /// </summary>
    protected abstract Task OnTick();
    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="stoppingToken">Indicates that the shutdown process should no longer be graceful.</param>
    protected virtual Task OnStop(CancellationToken stoppingToken) { return Task.CompletedTask; }

    internal void TimerCallback(object state)
    {
      if (CancellationToken.IsCancellationRequested)
        return;

      CleanTasks();

      if (CallbacksCanOverlap)
      {
        _onTickTasks.Add(Task.Run(OnTick, CancellationToken));
        return;
      }

      // the lock guarantee to not overlap the callbacks
      var hasLock = false;
      try
      {
        Monitor.TryEnter(_timerLock, ref hasLock);
        if (!hasLock)
          return;

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        var task = Task.Run(OnTick, CancellationToken);
        _onTickTasks.Add(task);
        task.Wait();
      }
      catch (Exception) { }
      finally
      {
        if (hasLock)
        {
          Monitor.Exit(_timerLock);
          if (!CancellationToken.IsCancellationRequested)
            _timer?.Change(TimerInterval, TimerInterval);
        }
      }
    }

    private void CleanTasks()
    {
      foreach (var task in _onTickTasks)
      {
        if (task.Status >= TaskStatus.RanToCompletion)
        {
          _onTickTasks.TryTake(out Task _);
        }
      }
    }

    public Task StartAsync(CancellationToken startingToken)
    {
      _cts = new CancellationTokenSource();

      _onStartTask = Task.Run(() => OnStart(startingToken), startingToken);
      _timer = new Timer(TimerCallback, null, InitialDelay, TimerInterval);

      return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
      try
      {
        _cts?.Cancel();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _onStopTask = Task.Run(() => OnStop(stoppingToken), stoppingToken);
      }
      finally
      {
        // Wait until the tasks completes

        foreach (var t in _onTickTasks)
        {
          try
          {
            if (t?.Status < TaskStatus.RanToCompletion)
              await t;
          }
          catch (TaskCanceledException)
          {
            // supress
          }
        }

        try
        {
          if (_onStartTask?.Status < TaskStatus.RanToCompletion)
            await _onStartTask;
        }
        catch (TaskCanceledException) { }

        try
        {
          if (_onStopTask?.Status < TaskStatus.RanToCompletion)
            await _onStopTask;
        }
        catch (TaskCanceledException) { }

      }
    }

    public virtual void Dispose()
    {
      _cts?.Cancel();
      _timer?.Dispose();
    }
  }
}

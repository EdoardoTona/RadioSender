using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

// Adapts existing decoders; there is no dispatcher filtering or implicit deduplication here.
public sealed class ProtocolSourceModule(ModuleContext context,
  Func<IDispatchSink, IRadioSenderHost> factory, bool radio = false) : FlowModule, IDispatchSink
{
  private readonly Channel<Punch> _input = Channel.CreateBounded<Punch>(256);
  private readonly CancellationTokenSource _lifetime = new();
  private IRadioSenderHost? _host;
  private Task? _pump, _start, _ping;
  private bool _stopped, _disposed;
  private readonly RadioNetwork _network = new();
  ILogger IDispatchSink.Logger => Logger;
  void IDispatchSink.SetSourceState(string status, string? detail) { if (!_stopped) SetState(status, detail); }

  public override Task StartAsync(CancellationToken ct)
  {
    _pump = PumpAsync();
    SetState("Starting");
    _start = StartHostAsync();
    return Task.CompletedTask;
  }
  private async Task StartHostAsync()
  {
    while (!_lifetime.IsCancellationRequested)
    {
      try
      {
        _host = factory(this);
        await _host.StartAsync(_lifetime.Token);
        if (State.Status is "Starting" or "Disconnected") SetState("Running");
        return;
      }
      catch (Exception e) when (!_lifetime.IsCancellationRequested)
      {
        SetState("Disconnected", $"{e.Message} Retrying.");
        if (_host is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else if (_host is IDisposable disposable) disposable.Dispose();
        _host = null;
        try { await Task.Delay(2000, _lifetime.Token); }
        catch (OperationCanceledException) { return; }
      }
      catch (Exception) when (_lifetime.IsCancellationRequested) { return; }
    }
  }
  public void PushDispatch(PunchDispatch dispatch)
  {
    if (_lifetime.IsCancellationRequested) return;
    if (radio) _network.Update(dispatch);
    foreach (var punch in dispatch.Punches ?? [])
      if (!_input.Writer.TryWrite(punch)) Logger.Warning("Decoder input queue full: event rejected for {CompetitorId}.", punch.CompetitorId);
  }
  public void PushDispatches(IEnumerable<PunchDispatch> dispatches)
  { foreach (var dispatch in dispatches) PushDispatch(dispatch); }
  private async Task PumpAsync()
  {
    try { await foreach (var punch in _input.Reader.ReadAllAsync(_lifetime.Token)) await context.Output.PublishAsync(punch, _lifetime.Token); }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
  }
  public override object? ViewData() => radio ? _network.Snapshot() : null;
  public override async ValueTask<CommandResult> ExecuteCommandAsync(string command, JsonObject arguments, CancellationToken ct)
  {
    if (command == "ping" && _host is Hosts.Source.TmFRadio.TmFRadioGateway gateway)
    {
      if (_ping == null || _ping.IsCompleted)
        _ping = Task.Run(async () =>
        {
          try { await gateway.CheckPathAndStatus(ct: _lifetime.Token); }
          catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
          catch (Exception e) { Logger.Warning(e, "Unable to request radio status and path."); }
        }, CancellationToken.None);
      return new("Radio status and path requested.");
    }
    return await base.ExecuteCommandAsync(command, arguments, ct);
  }
  public override async Task StopAsync(CancellationToken ct)
  {
    if (_stopped) return;
    _stopped = true;
    await _lifetime.CancelAsync();
    _input.Writer.TryComplete();
    try
    {
      if (_start != null) await _start;
      if (_host != null) await _host.StopAsync(ct);
    }
    finally
    {
      if (_pump != null) await _pump;
      if (_ping != null) await _ping;
      if (_host is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
      else if (_host is IDisposable disposable) disposable.Dispose();
      SetState("Stopped");
    }
  }
  public override async ValueTask DisposeAsync() { if (_disposed) return; await StopAsync(default); _disposed = true; _lifetime.Dispose(); }
}

internal sealed class SourceHttpClientFactory(string host) : IHttpClientFactory
{
  public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri(host), Timeout = TimeSpan.FromSeconds(10) };
}

public sealed class RadioNetwork
{
  private readonly object _sync = new();
  private readonly Dictionary<string, NodeNew> _nodes = [];
  private readonly Dictionary<string, Hop> _hops = [];
  public void Update(PunchDispatch dispatch)
  {
    lock (_sync)
    {
      foreach (var node in dispatch.Nodes ?? []) { _nodes[node.Id] = node; if (_nodes.Count > 256) _nodes.Remove(_nodes.Keys.First()); }
      foreach (var hop in dispatch.Hops ?? []) { _hops[hop.Id] = hop; if (_hops.Count > 512) _hops.Remove(_hops.Keys.First()); }
    }
  }
  public object Snapshot() { lock (_sync) return new { Nodes = _nodes.Values.ToArray(), Hops = _hops.Values.ToArray() }; }
}

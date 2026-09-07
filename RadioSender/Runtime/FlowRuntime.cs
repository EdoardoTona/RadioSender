using Microsoft.Extensions.Hosting;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RadioSender.Runtime;

internal sealed record FlowEvent(Punch Punch, Guid EventId, Guid ExecutionId, long? ReplayOf = null);
public sealed record RuntimeNode(string Id, string Name, string Type, string Status, string? Detail, int Pending);
public sealed record RuntimeEdge(string Id, long Forwarded, long Filtered, int Pending, long Rejected);
public sealed record RuntimeGraph(Guid? DocumentId, string? Path, long Revision, FlowDocument Document);
public sealed record RuntimeSnapshot(Guid SessionId, Guid? DocumentId, string? Path, long Revision, bool Running,
  string? Error, IReadOnlyList<RuntimeNode> Nodes, IReadOnlyList<RuntimeEdge> Edges);
public sealed record ApplyResult(long Revision, string[] Restarted, string[] Kept);
public sealed record ApplyPreview(string[] Started, string[] Kept, string[] Stopped);
public sealed record ReplayRequest(Guid SessionId, long Revision, Guid OperationId, long[] ObservationIds, string Port = "out");
public sealed record ReplayResult(Guid OperationId, int Events);

public sealed class FlowRuntime(ModuleRegistry registry, FlowValidator validator, FlowJournal journal) : IHostedService, IAsyncDisposable
{
  private sealed class Instance(FlowNode node, JsonObject settings, string signature, Guid generation, FlowModule module)
  {
    public FlowNode Node = node;
    public JsonObject Settings = settings;
    public string Signature = signature;
    public Guid Generation = generation;
    public FlowModule Module = module;
    public TargetQueue? Target;
  }
  private sealed class EdgeState(FlowEdge edge, Filter? filter)
  {
    public FlowEdge Edge = edge;
    public Filter? Filter = filter;
    public DelayedEdgeQueue? Delay;
    public long Forwarded;
    public long Filtered;
    public long Rejected;
  }
  private sealed record Work(Func<Task> Action, TaskCompletionSource? Completion);
  private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(256)
  { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
  private readonly object _snapshotLock = new();
  private Dictionary<string, Instance> _instances = [];
  private Dictionary<string, EdgeState[]> _outgoing = [];
  private EdgeState[] _edges = [];
  private readonly Dictionary<Guid, (string Fingerprint, ReplayResult Result)> _replays = [];
  private Task? _worker;
  private Guid _sessionId = Guid.NewGuid();
  private Guid? _documentId;
  private string? _path;
  private long _revision;
  private string? _error;
  private bool _running;
  private FlowDocument _activeDocument = new();

  public RuntimeGraph Graph()
  { lock (_snapshotLock) return new(_documentId, _path, _revision, FlowJson.Clone(_activeDocument)); }

  public Task StartAsync(CancellationToken cancellationToken) { _worker = RunAsync(); return Task.CompletedTask; }
  private async Task RunAsync()
  {
    await foreach (var work in _queue.Reader.ReadAllAsync())
    {
      try { await work.Action(); work.Completion?.TrySetResult(); }
      catch (Exception e)
      { lock (_snapshotLock) _error = e.Message; work.Completion?.TrySetException(e); }
    }
  }

  private async Task ExecuteAsync(Func<Task> action, CancellationToken ct)
  {
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await _queue.Writer.WriteAsync(new(action, completion), ct);
    // Once accepted, complete the operation even if its HTTP caller disconnects.
    await completion.Task;
  }

  private sealed class Output(FlowRuntime runtime, string nodeId, Guid generation) : IFlowOutput
  {
    public async ValueTask PublishAsync(Punch punch, CancellationToken cancellationToken)
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(TimeSpan.FromSeconds(2));
      try
      {
        await runtime._queue.Writer.WriteAsync(new(async () =>
        {
          if (!runtime._instances.TryGetValue(nodeId, out var instance) || instance.Generation != generation) return;
          await runtime.RouteOutputAsync(nodeId, new(punch, Guid.NewGuid(), Guid.NewGuid()));
        }, null), timeout.Token);
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        lock (runtime._snapshotLock) runtime._error = $"Input queue full: an event from {nodeId} was rejected.";
        Log.ForContext("NodeId", nodeId).ForContext("SessionId", runtime._sessionId)
          .Warning("Input queue full: event rejected.");
      }
    }
  }

  public RuntimeSnapshot Snapshot()
  {
    lock (_snapshotLock)
      return new(_sessionId, _documentId, _path, _revision, _running, _error,
        _instances.Values.Select(i => new RuntimeNode(i.Node.Id, i.Node.Name, i.Node.Type, i.Module.State.Status,
          i.Module.State.Detail, i.Target?.Pending ?? 0)).ToArray(),
        _edges.Select(e => new RuntimeEdge(e.Edge.Id, Interlocked.Read(ref e.Forwarded), Interlocked.Read(ref e.Filtered), e.Delay?.Pending ?? 0, Interlocked.Read(ref e.Rejected))).ToArray());
  }

  private static string Signature(FlowNode node, JsonObject settings, string directory) =>
    node.Type + "|" + settings.ToJsonString(FlowJson.Options) +
      (node.Type == "target.file" ? "|" + Path.GetFullPath(settings["path"]!.GetValue<string>(), directory) : "");

  public async Task<ApplyPreview> PreviewAsync(Guid documentId, string path, FlowDocument document, CancellationToken ct)
  {
    document = FlowJson.Clone(document);
    var issues = validator.Validate(document);
    if (issues.Count > 0) throw new FlowException(string.Join("\n", issues.Select(i => i.Message)));
    ApplyPreview? result = null;
    await ExecuteAsync(() =>
    {
      var enabled = document.Nodes.Where(n => n.Enabled).ToArray();
      var kept = enabled.Where(n => _documentId == documentId && _instances.TryGetValue(n.Id, out var old) &&
        old.Signature == Signature(n, registry.Find(n.Type)!.Normalize(n.Settings), Path.GetDirectoryName(path)!)).Select(n => n.Id).ToHashSet();
      result = new(enabled.Where(n => !kept.Contains(n.Id)).Select(n => n.Name).ToArray(),
        enabled.Where(n => kept.Contains(n.Id)).Select(n => n.Name).ToArray(),
        _instances.Values.Where(n => !kept.Contains(n.Node.Id)).Select(n => n.Node.Name).ToArray());
      return Task.CompletedTask;
    }, ct);
    return result!;
  }

  public async Task<ApplyResult> ApplyAsync(Guid documentId, string path, long revision, FlowDocument document, CancellationToken ct = default)
  {
    document = FlowJson.Clone(document);
    var issues = validator.Validate(document);
    if (issues.Count > 0) throw new FlowException(string.Join("\n", issues.Select(i => $"{i.ElementId}: {i.Message}")));
    ApplyResult? result = null;
    await ExecuteAsync(async () =>
    {
      if (_documentId == documentId && revision < _revision)
        throw new FlowException("A newer revision of this document is already running.", 409);
      var directory = Path.GetDirectoryName(path)!;
      var sameDocument = _documentId == documentId;
      var nextSession = sameDocument ? _sessionId : Guid.NewGuid();
      var next = new Dictionary<string, Instance>();
      var fresh = new List<Instance>();
      var kept = new List<string>();
      foreach (var node in document.Nodes.Where(n => n.Enabled))
      {
        var definition = registry.Find(node.Type)!;
        var settings = definition.Normalize(node.Settings);
        var signature = Signature(node, settings, directory);
        if (sameDocument && _instances.TryGetValue(node.Id, out var old) && old.Signature == signature)
        { next[node.Id] = old; kept.Add(node.Id); }
        else
        {
          var generation = Guid.NewGuid();
          var instance = new Instance(node, settings, signature, generation,
            definition.Create(settings, new(node.Id, directory, new Output(this, node.Id, generation), ResolveModule)));
          next[node.Id] = instance; fresh.Add(instance);
        }
      }

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
      try { await DrainAsync(timeout.Token); }
      catch { foreach (var i in fresh) await i.Module.DisposeAsync(); throw new FlowException("Apply timed out while draining delayed events and target queues. The current flow is unchanged.", 409); }
      var retired = _instances.Values.Where(i => !kept.Contains(i.Node.Id)).ToArray();
      try
      {
        foreach (var instance in retired) await RetireAsync(instance);
        foreach (var instance in fresh.OrderBy(i => registry.Find(i.Node.Type)!.Descriptor.Category == "Source"))
          await StartInstanceAsync(instance, nextSession);
      }
      catch (Exception failure)
      {
        foreach (var instance in fresh) await RetireAsync(instance);
        var rollbackErrors = new List<string>();
        foreach (var old in retired)
        {
          try
          {
            var generation = Guid.NewGuid();
            old.Generation = generation;
            old.Module = registry.Find(old.Node.Type)!.Create(old.Settings,
              new(old.Node.Id, Path.GetDirectoryName(_path!)!, new Output(this, old.Node.Id, generation), ResolveModule));
            old.Target = null;
            await StartInstanceAsync(old, _sessionId);
          }
          catch (Exception e) { rollbackErrors.Add($"{old.Node.Name}: {e.Message}"); }
        }
        throw new FlowException("Apply failed: " + failure.Message +
          (rollbackErrors.Count == 0 ? " The previous flow was restored." : " Restore failed: " + string.Join("; ", rollbackErrors)), 409);
      }

      foreach (var edge in _edges.Where(e => e.Delay != null)) await edge.Delay!.DisposeAsync();
      lock (_snapshotLock)
      {
        if (!sameDocument) { _sessionId = nextSession; journal.Clear(); _replays.Clear(); }
        foreach (var node in document.Nodes.Where(n => next.ContainsKey(n.Id))) next[node.Id].Node = node;
        _instances = next;
        var filters = document.Filters.ToDictionary(f => f.Id, f => f.Rules.Compile());
        _edges = document.Edges.Where(e => e.Enabled && next.ContainsKey(e.From.Node) && next.ContainsKey(e.To.Node))
          .Select(e => new EdgeState(e, e.FilterId == null ? null : filters[e.FilterId])).ToArray();
        _outgoing = _edges.GroupBy(e => e.Edge.From.Node).ToDictionary(g => g.Key, g => g.ToArray());
        _documentId = documentId; _path = path; _revision = revision; _error = null; _running = true;
        _activeDocument = document;
      }
      foreach (var edge in _edges.Where(e => e.Edge.DelayMs > 0))
      {
        edge.Delay = new(edge.Edge.DelayMs, item => DeliverEdgeAsync(edge, item), (item, reason) =>
        {
          Interlocked.Increment(ref edge.Rejected);
          Log.ForContext("NodeId", edge.Edge.From.Node).ForContext("EdgeId", edge.Edge.Id).ForContext("SessionId", nextSession)
            .Warning("Delayed event {CompetitorId} rejected: {Reason}", item.Punch.CompetitorId, reason);
        });
        edge.Delay.Start();
      }
      Log.Information("Applied flow {Path}, revision {Revision}: {NodeCount} nodes.", path, revision, next.Count);
      result = new(revision, fresh.Select(i => i.Node.Id).ToArray(), kept.ToArray());
    }, ct);
    return result!;
  }

  private FlowModule? ResolveModule(string id) => _instances.GetValueOrDefault(id)?.Module;

  private async Task StartInstanceAsync(Instance instance, Guid sessionId)
  {
    instance.Module.AttachLogging(instance.Node.Id, sessionId);
    await instance.Module.StartAsync(CancellationToken.None);
    if (registry.Find(instance.Node.Type)!.Descriptor.Category == "Target")
    {
      instance.Target = new(instance.Module, (item, edgeId, revision, result) =>
      {
        journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, instance.Node.Id, "delivery", edgeId,
          revision, item.Punch, result.Status, result.Detail);
        if (result.Status is "Failed" or "Rejected" or "Partial" or "Suppressed")
          Log.ForContext("NodeId", instance.Node.Id).ForContext("EdgeId", edgeId).ForContext("SessionId", sessionId)
            .Warning("Delivery {Status}: {Detail}", result.Status, result.Detail);
      });
      instance.Target.Start();
    }
  }

  private static async Task RetireAsync(Instance instance)
  {
    if (instance.Target != null) { await instance.Target.DisposeAsync(); instance.Target = null; }
    await instance.Module.StopAsync(CancellationToken.None);
    await instance.Module.DisposeAsync();
  }

  private async Task RouteOutputAsync(string nodeId, FlowEvent item)
  {
    journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, nodeId, "output", null, _revision, item.Punch);
    if (!_outgoing.TryGetValue(nodeId, out var edges)) return;
    await Task.WhenAll(edges.Select(async state =>
    {
      var transformed = state.Filter == null ? item.Punch : state.Filter.Transform(item.Punch);
      if (transformed == null) { Interlocked.Increment(ref state.Filtered); return; }
      var forwarded = item with { Punch = transformed };
      if (state.Delay != null) await state.Delay.EnqueueAsync(forwarded);
      else await DeliverEdgeAsync(state, forwarded);
    }));
  }

  private async Task DeliverEdgeAsync(EdgeState state, FlowEvent item)
  {
    Interlocked.Increment(ref state.Forwarded);
    var target = _instances[state.Edge.To.Node];
    journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, target.Node.Id, "input", state.Edge.Id, _revision, item.Punch);
    if (target.Target != null) await target.Target.EnqueueAsync(item, state.Edge.Id, _revision);
    else
    {
      Punch? processed;
      try { processed = target.Module.Process(item.Punch, item.ReplayOf != null); }
      catch (Exception e)
      {
        journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, target.Node.Id, "processing", state.Edge.Id, _revision, item.Punch, "Failed", e.Message);
        Log.ForContext("NodeId", target.Node.Id).ForContext("SessionId", _sessionId).Warning("Processing failed: {Reason}", e.Message);
        return;
      }
      if (processed != null) await RouteOutputAsync(target.Node.Id, item with { Punch = processed });
      else journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, target.Node.Id, "processing", state.Edge.Id, _revision, item.Punch, "Suppressed", "Duplicate event.");
    }
  }

  private async Task DrainAsync(CancellationToken ct)
  {
    do
    {
      await Task.WhenAll(_edges.Where(e => e.Delay != null).Select(e => e.Delay!.DrainAsync(ct)));
      await Task.WhenAll(_instances.Values.Where(i => i.Target != null).Select(i => i.Target!.DrainAsync(ct)));
      // An upstream delayed edge may have enqueued another edge after its first drain snapshot.
    } while (_edges.Any(e => e.Delay?.Pending > 0) || _instances.Values.Any(i => i.Target?.Pending > 0));
  }

  public async Task<CommandResult> ExecuteCommandAsync(Guid sessionId, long revision, string nodeId, string command, JsonObject arguments, CancellationToken ct = default)
  {
    CommandResult? result = null;
    await ExecuteAsync(async () =>
    {
      CheckSession(sessionId, revision);
      if (!_instances.TryGetValue(nodeId, out var node) ||
          !registry.Find(node.Node.Type)!.Descriptor.Commands.Any(c => c.Id == command))
        throw new FlowException("This running node does not declare the requested command.", 409);
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      result = await node.Module.ExecuteCommandAsync(command, arguments, timeout.Token);
      foreach (var punch in result.Output ?? [])
        await RouteOutputAsync(nodeId, new(punch, Guid.NewGuid(), Guid.NewGuid()));
      Log.ForContext("NodeId", nodeId).ForContext("SessionId", _sessionId)
        .Information("Command {Command}: {Message}", command, result.Message);
    }, ct);
    return result! with { Output = null };
  }

  public async Task<object?> ViewAsync(Guid sessionId, long revision, string nodeId, CancellationToken ct = default)
  {
    object? result = null;
    await ExecuteAsync(() =>
    {
      CheckSession(sessionId, revision);
      if (!_instances.TryGetValue(nodeId, out var instance)) throw new FlowException("This node is not running.", 409);
      result = instance.Module.ViewData();
      return Task.CompletedTask;
    }, ct);
    return result;
  }

  private void CheckSession(Guid session, long revision)
  {
    if (!_running || session != _sessionId || revision != _revision)
      throw new FlowException("The running flow changed. Refresh the inspector before sending or replaying.", 409);
  }

  public async Task<ReplayResult> ReplayAsync(string nodeId, ReplayRequest request, bool retry = false, CancellationToken ct = default)
  {
    ReplayResult? result = null;
    await ExecuteAsync(async () =>
    {
      CheckSession(request.SessionId, request.Revision);
      if (request.OperationId == Guid.Empty || request.ObservationIds == null) throw new FlowException("An operation ID and observation selection are required.");
      var fingerprint = nodeId + "|" + retry + "|" + JsonSerializer.Serialize(request);
      if (_replays.TryGetValue(request.OperationId, out var previous))
      {
        if (fingerprint != previous.Fingerprint) throw new FlowException("This operation ID was already used for another replay.", 409);
        result = previous.Result; return;
      }
      if (!_instances.TryGetValue(nodeId, out var instance) || (retry ? instance.Target == null : instance.Target != null) || request.Port != (retry ? "in" : "out"))
        throw new FlowException("This node/port does not support the requested operation.");
      var items = journal.Resolve(nodeId, retry ? "input" : "output", request.ObservationIds);
      if (_replays.Count >= 4096) throw new FlowException("The session replay limit was reached. Start a new runtime session to continue.", 409);
      result = new(request.OperationId, items.Length);
      _replays[request.OperationId] = (fingerprint, result);
      Log.ForContext("NodeId", nodeId).ForContext("SessionId", _sessionId)
        .Information("{Operation}: {Count} selected events.", retry ? "Target retry" : "Output replay", items.Length);
      foreach (var observation in items)
      {
        var item = new FlowEvent(observation.Punch, observation.EventId, request.OperationId, observation.Id);
        if (retry)
        {
          journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, nodeId, "input", observation.EdgeId, _revision, item.Punch);
          await instance.Target!.EnqueueAsync(item, observation.EdgeId ?? "", _revision);
        }
        else await RouteOutputAsync(nodeId, item);
      }
    }, ct);
    return result!;
  }

  public Task StopFlowAsync(CancellationToken ct = default) => ExecuteAsync(async () =>
  {
    foreach (var edge in _edges.Where(e => e.Delay != null)) await edge.Delay!.DisposeAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await Task.WhenAll(_instances.Values.Where(i => i.Target != null).Select(i => i.Target!.DrainAsync(timeout.Token))); }
    catch (OperationCanceledException) { Log.Warning("Stop timed out while draining targets. Remaining deliveries will be cancelled."); }
    foreach (var instance in _instances.Values) await RetireAsync(instance);
    lock (_snapshotLock) { _instances = []; _outgoing = []; _edges = []; _running = false; _documentId = null; _error = null; }
    Log.Information("Flow stopped.");
  }, ct);

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    if (_worker == null) return;
    await StopFlowAsync(cancellationToken);
    _queue.Writer.TryComplete();
    await _worker;
  }
  public async ValueTask DisposeAsync()
  { if (_worker != null && !_worker.IsCompleted) await StopAsync(CancellationToken.None); }
}

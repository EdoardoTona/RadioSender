using Microsoft.Extensions.Hosting;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
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
public sealed record RuntimeEdge(string Id, long Forwarded, long Filtered);
public sealed record RuntimeSnapshot(Guid SessionId, Guid? DocumentId, string? Path, long Revision, bool Running,
  string? Error, IReadOnlyList<RuntimeNode> Nodes, IReadOnlyList<RuntimeEdge> Edges);
public sealed record ApplyResult(long Revision, string[] Restarted, string[] Kept);
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
  private sealed class EdgeState(FlowEdge edge)
  {
    public FlowEdge Edge = edge;
    public Filter? Filter = edge.Filter?.Compile();
    public long Forwarded;
    public long Filtered;
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
      { lock (runtime._snapshotLock) runtime._error = $"Input queue full: an event from {nodeId} was rejected."; }
    }
  }

  public RuntimeSnapshot Snapshot()
  {
    lock (_snapshotLock)
      return new(_sessionId, _documentId, _path, _revision, _running, _error,
        _instances.Values.Select(i => new RuntimeNode(i.Node.Id, i.Node.Name, i.Node.Type, i.Module.State.Status,
          i.Module.State.Detail, i.Target?.Pending ?? 0)).ToArray(),
        _edges.Select(e => new RuntimeEdge(e.Edge.Id, Interlocked.Read(ref e.Forwarded), Interlocked.Read(ref e.Filtered))).ToArray());
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
      var next = new Dictionary<string, Instance>();
      var fresh = new List<Instance>();
      var kept = new List<string>();
      foreach (var node in document.Nodes.Where(n => n.Enabled))
      {
        var definition = registry.Find(node.Type)!;
        var settings = definition.Normalize(node.Settings);
        var signature = node.Type + "|" + settings.ToJsonString(FlowJson.Options) +
          (node.Type == "target.file" ? "|" + Path.GetFullPath(settings["path"]!.GetValue<string>(), directory) : "");
        if (sameDocument && _instances.TryGetValue(node.Id, out var old) && old.Signature == signature)
        { next[node.Id] = old; kept.Add(node.Id); }
        else
        {
          var generation = Guid.NewGuid();
          var instance = new Instance(node, settings, signature, generation,
            definition.Create(settings, new(node.Id, directory, new Output(this, node.Id, generation))));
          next[node.Id] = instance; fresh.Add(instance);
        }
      }

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
      try { await Task.WhenAll(_instances.Values.Where(i => i.Target != null).Select(i => i.Target!.DrainAsync(timeout.Token))); }
      catch { foreach (var i in fresh) await i.Module.DisposeAsync(); throw new FlowException("Apply timed out while draining target queues. The current flow is unchanged.", 409); }
      var retired = _instances.Values.Where(i => !kept.Contains(i.Node.Id)).ToArray();
      try
      {
        foreach (var instance in retired) await RetireAsync(instance);
        foreach (var instance in fresh.OrderBy(i => registry.Find(i.Node.Type)!.Descriptor.Category == "Source"))
          await StartInstanceAsync(instance);
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
              new(old.Node.Id, Path.GetDirectoryName(_path!)!, new Output(this, old.Node.Id, generation)));
            old.Target = null;
            await StartInstanceAsync(old);
          }
          catch (Exception e) { rollbackErrors.Add($"{old.Node.Name}: {e.Message}"); }
        }
        throw new FlowException("Apply failed: " + failure.Message +
          (rollbackErrors.Count == 0 ? " The previous flow was restored." : " Restore failed: " + string.Join("; ", rollbackErrors)), 409);
      }

      lock (_snapshotLock)
      {
        if (!sameDocument) { _sessionId = Guid.NewGuid(); journal.Clear(); _replays.Clear(); }
        foreach (var node in document.Nodes.Where(n => next.ContainsKey(n.Id))) next[node.Id].Node = node;
        _instances = next;
        _edges = document.Edges.Where(e => e.Enabled && next.ContainsKey(e.From.Node) && next.ContainsKey(e.To.Node)).Select(e => new EdgeState(e)).ToArray();
        _outgoing = _edges.GroupBy(e => e.Edge.From.Node).ToDictionary(g => g.Key, g => g.ToArray());
        _documentId = documentId; _path = path; _revision = revision; _error = null; _running = true;
      }
      result = new(revision, fresh.Select(i => i.Node.Id).ToArray(), kept.ToArray());
    }, ct);
    return result!;
  }

  private async Task StartInstanceAsync(Instance instance)
  {
    await instance.Module.StartAsync(CancellationToken.None);
    if (registry.Find(instance.Node.Type)!.Descriptor.Category == "Target")
    {
      instance.Target = new(instance.Module, (item, edgeId, revision, result) =>
        journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, instance.Node.Id, "delivery", edgeId,
          revision, item.Punch, result.Status, result.Detail));
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
      Interlocked.Increment(ref state.Forwarded);
      var forwarded = item with { Punch = transformed };
      var target = _instances[state.Edge.To.Node];
      journal.Add(item.EventId, item.ExecutionId, item.ReplayOf, target.Node.Id, "input", state.Edge.Id, _revision, transformed);
      if (target.Target != null) await target.Target.EnqueueAsync(forwarded, state.Edge.Id, _revision);
      else await RouteOutputAsync(target.Node.Id, forwarded);
    }));
  }

  public Task SendManualAsync(Guid sessionId, long revision, string nodeId, Punch punch, CancellationToken ct = default) => ExecuteAsync(async () =>
  {
    CheckSession(sessionId, revision);
    if (!_instances.TryGetValue(nodeId, out var node) || node.Node.Type != "source.manual")
      throw new FlowException("Apply and start a Manual input node before sending.", 409);
    if (punch == null || string.IsNullOrWhiteSpace(punch.CompetitorId) || punch.CompetitorId.Length > 256 || punch.Time == default ||
        !Enum.IsDefined(punch.CompetitorIdType) || !Enum.IsDefined(punch.ControlType) || !Enum.IsDefined(punch.CompetitorStatus) || punch.Control < 0)
      throw new FlowException("Enter a competitor ID, valid time, control and event types.");
    punch = punch with { SourceId = nodeId, ReceivedAt = DateTimeOffset.UtcNow, Competitor = null };
    await RouteOutputAsync(nodeId, new(punch, Guid.NewGuid(), Guid.NewGuid()));
  }, ct);

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
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await Task.WhenAll(_instances.Values.Where(i => i.Target != null).Select(i => i.Target!.DrainAsync(timeout.Token)));
    foreach (var instance in _instances.Values) await RetireAsync(instance);
    lock (_snapshotLock) { _instances = []; _outgoing = []; _edges = []; _running = false; _documentId = null; }
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

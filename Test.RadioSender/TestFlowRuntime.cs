using NUnit.Framework;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using RadioSender.Runtime;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Test.RadioSender;

[TestFixture]
public class TestFlowRuntime : IAsyncDisposable
{
  private FlowRuntime _runtime = null!;
  private FlowJournal _journal = null!;
  private string _directory = null!;
  private readonly Guid _documentId = Guid.NewGuid();
  public ValueTask DisposeAsync() => _runtime?.DisposeAsync() ?? ValueTask.CompletedTask;

  [SetUp]
  public async Task SetUp()
  {
    _directory = Path.Combine(Path.GetTempPath(), "radiosender-flow-test-" + Guid.NewGuid());
    Directory.CreateDirectory(_directory);
    var registry = ModuleRegistry.CreateDefault();
    _journal = new();
    _runtime = new(registry, new(registry), _journal);
    await _runtime.StartAsync(default);
  }

  [TearDown]
  public async Task TearDown()
  { await _runtime.DisposeAsync(); Directory.Delete(_directory, true); }

  internal static FlowNode Node(string id, string type, object? settings = null) => new()
  { Id = id, Name = id, Type = type, Settings = JsonSerializer.SerializeToNode(settings ?? new { }, FlowJson.Options)!.AsObject() };
  internal static FlowEdge Edge(string id, string from, string to, EdgeFilter? filter = null) => new()
  { Id = id, From = new(from, "out"), To = new(to, "in"), Filter = filter };
  private FlowDocument Branches(int mapped = 1) => new()
  {
    Nodes = [Node("manual", "source.manual"), Node("after", "processor.passthrough"),
      Node("mapped", "target.file", new FileSettings { Path = "mapped.csv", Format = "{Control}{CRLF}" }),
      Node("raw", "target.file", new FileSettings { Path = "raw.csv", Format = "{Control}{CRLF}" })],
    Edges = [Edge("mapping", "manual", "after", new() { MapControls = new() { ["35"] = mapped } }),
      Edge("output", "after", "mapped"), Edge("raw", "manual", "raw")]
  };
  private Task<ApplyResult> Apply(FlowDocument document, long revision = 1, Guid? documentId = null) =>
    _runtime.ApplyAsync(documentId ?? _documentId, Path.Combine(_directory, "flow.json"), revision, document);
  private async Task Send(int control = 35)
  {
    var snapshot = _runtime.Snapshot();
    await _runtime.SendManualAsync(snapshot.SessionId, snapshot.Revision, "manual",
      new("123", new DateTime(2026, 9, 6, 12, 34, 56), control, "ignored", DateTimeOffset.UtcNow, CompetitorIdType.PunchingCard, PunchControlType.Control));
  }
  private async Task<global::RadioSender.Runtime.ReplayResult> Replay(string node, long observationId, Guid operationId)
  {
    var state = _runtime.Snapshot();
    return await _runtime.ReplayAsync(node, new(state.SessionId, state.Revision, operationId, [observationId]));
  }
  private static async Task WaitUntil(Func<bool> condition)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition()) await Task.Delay(10, timeout.Token);
  }

  [Test]
  public async Task BranchMappings_InspectActualValues_AndReplayFromSelectedOutput()
  {
    await Apply(Branches());
    await Send();
    var source = _journal.Read("manual", "output").Items.Single();
    var passthrough = _journal.Read("after", "output").Items.Single();
    Assert.That(source.Punch.Control, Is.EqualTo(35));
    Assert.That(passthrough.Punch.Control, Is.EqualTo(1));
    Assert.That(_journal.Read("mapped", "input").Items.Single().Punch.Control, Is.EqualTo(1));
    Assert.That(_journal.Read("raw", "input").Items.Single().Punch.Control, Is.EqualTo(35));

    var applied = await Apply(Branches(2), 2);
    Assert.That(applied.Restarted, Is.Empty, "Changing an edge filter must not reopen modules.");
    await Replay("manual", source.Id, Guid.NewGuid());
    await Replay("after", passthrough.Id, Guid.NewGuid());
    await _runtime.StopFlowAsync();

    Assert.That(File.ReadAllLines(Path.Combine(_directory, "mapped.csv")), Is.EqualTo(new[] { "1", "2", "1" }));
    Assert.That(File.ReadAllLines(Path.Combine(_directory, "raw.csv")), Is.EqualTo(new[] { "35", "35" }));
    Assert.That(source.Punch.Control, Is.EqualTo(35), "Historical snapshots must stay unchanged.");
    Assert.That(_journal.Read("after", "output").Items.Last().ReplayOf, Is.EqualTo(passthrough.Id));
  }

  [Test]
  public async Task ReplayRetryWithSameOperationId_DoesNotSendTwice_AndStaleRevisionIsRejected()
  {
    await Apply(Branches()); await Send();
    var observation = _journal.Read("manual", "output").Items.Single();
    var operation = Guid.NewGuid();
    await Replay("manual", observation.Id, operation);
    await Replay("manual", observation.Id, operation);
    var old = _runtime.Snapshot();
    await Apply(Branches(2), 2);
    Assert.ThrowsAsync<FlowException>(async () => await _runtime.ReplayAsync("manual", new(old.SessionId, old.Revision, Guid.NewGuid(), [observation.Id])));
    await _runtime.StopFlowAsync();
    Assert.That(File.ReadAllLines(Path.Combine(_directory, "raw.csv")), Has.Length.EqualTo(2));
  }

  [Test]
  public async Task ApplyingAnotherDocument_CannotReplayOldObservationsWithReusedNodeIds()
  {
    await Apply(Branches()); await Send();
    var observation = _journal.Read("manual", "output").Items.Single();
    var oldSession = _runtime.Snapshot().SessionId;
    await Apply(Branches(), 1, Guid.NewGuid());
    Assert.That(_runtime.Snapshot().SessionId, Is.Not.EqualTo(oldSession));
    Assert.That(_journal.Read("manual", "output").Items, Is.Empty);
    Assert.ThrowsAsync<FlowException>(async () => await Replay("manual", observation.Id, Guid.NewGuid()));
  }

  [Test]
  public async Task FailedApply_RestoresPreviousFileModuleAndGraph()
  {
    await Apply(Branches()); await Send();
    var candidate = Branches(8);
    candidate.Nodes[2] = candidate.Nodes[2] with { Settings = JsonSerializer.SerializeToNode(new FileSettings { Path = "missing/failure.csv" }, FlowJson.Options)!.AsObject() };
    Assert.ThrowsAsync<FlowException>(async () => await Apply(candidate, 2));
    Assert.That(_runtime.Snapshot().Revision, Is.EqualTo(1));
    await Send(); await _runtime.StopFlowAsync();
    Assert.That(File.ReadAllLines(Path.Combine(_directory, "mapped.csv")), Is.EqualTo(new[] { "1", "1" }));
  }

  [Test]
  public async Task DisconnectedTcpTarget_DoesNotPreventOtherBranch_AndReportsFailure()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var graph = Branches();
    graph.Nodes.Add(Node("tcp", "target.tcp", new TcpSettings { Port = port }));
    graph.Edges.Add(Edge("tcp", "manual", "tcp"));
    await Apply(graph); await Send();
    await WaitUntil(() => _journal.Read("tcp", "delivery").Items.Any());
    Assert.That(_journal.Read("tcp", "delivery").Items.Last().Status, Is.EqualTo("Failed"));
    await _runtime.StopFlowAsync();
    Assert.That(File.ReadAllLines(Path.Combine(_directory, "raw.csv")), Has.Length.EqualTo(1));
  }

  [Test]
  public async Task TcpSourceServer_HandlesFragmentedUtf8_AndReleasesPortOnRemoval()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    var graph = new FlowDocument
    {
      Nodes = [Node("tcp", "source.tcp", new TcpSettings { AsServer = true, Port = port }), Node("after", "processor.passthrough")],
      Edges = [Edge("stream", "tcp", "after")]
    };
    await Apply(graph);
    using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port);
    var bytes = Encoding.UTF8.GetBytes("é123;35;12:34:56,123\r\n");
    await client.GetStream().WriteAsync(bytes.AsMemory(0, 1));
    await client.GetStream().WriteAsync(bytes.AsMemory(1));
    await WaitUntil(() => _journal.Read("after", "input").Items.Any());
    Assert.That(_journal.Read("after", "input").Items.Single().Punch.CompetitorId, Is.EqualTo("é123"));
    await Apply(new(), 2);
    var reopened = new TcpListener(IPAddress.Loopback, port);
    try { reopened.Start(); } finally { reopened.Stop(); }
  }

  [Test]
  public async Task TcpTargetServer_WritesToConnectedReceiver_AndStopsCleanly()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    await Apply(new()
    {
      Nodes = [Node("manual", "source.manual"), Node("tcp", "target.tcp", new TcpSettings { Port = port, AsServer = true, Format = "{CompetitorId};{Control}{CRLF}" })],
      Edges = [Edge("stream", "manual", "tcp")]
    });
    using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port);
    // Accept runs independently of connect; wait for it before testing a delivery.
    await Task.Delay(50);
    await Send();
    using var reader = new StreamReader(client.GetStream());
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    Assert.That(await reader.ReadLineAsync(timeout.Token), Is.EqualTo("123;35"));
    await _runtime.StopFlowAsync();
  }

  [Test]
  public void GraphValidation_RejectsCyclesAndInvalidFilter_WithoutOpeningResources()
  {
    var registry = ModuleRegistry.CreateDefault();
    var graph = new FlowDocument
    {
      Nodes = [Node("a", "processor.passthrough"), Node("b", "processor.passthrough")],
      Edges = [Edge("ab", "a", "b", new() { MapControls = new() { ["invalid"] = 1 } }), Edge("ba", "b", "a")]
    };
    var issues = new FlowValidator(registry).Validate(graph);
    Assert.That(issues.Any(i => i.Field == "filter"), Is.True);
    Assert.That(issues.Any(i => i.Message.Contains("Cycles")), Is.True);
  }
}

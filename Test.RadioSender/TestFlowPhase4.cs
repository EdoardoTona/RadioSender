using NUnit.Framework;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hosts.Common;
using RadioSender.Runtime;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Test.RadioSender.TestFlowRuntime;

namespace Test.RadioSender;

[TestFixture]
public class TestFlowPhase4 : IAsyncDisposable
{
  public ValueTask DisposeAsync() => _runtime?.DisposeAsync() ?? ValueTask.CompletedTask;
  private FlowRuntime _runtime = null!;
  private FlowJournal _journal = null!;
  private string _directory = null!;
  private Guid _documentId;
  private static Punch Event(bool cancelled = false) => new("1234", new(2026, 9, 6, 12, 30, 0), 35,
    "test", DateTimeOffset.UtcNow, CompetitorIdType.PunchingCard, PunchControlType.Control, Cancellation: cancelled);
  [SetUp]
  public async Task Setup()
  {
    _directory = Path.Combine(Path.GetTempPath(), "radiosender-phase4-" + Guid.NewGuid());
    Directory.CreateDirectory(_directory); _documentId = Guid.NewGuid();
    var registry = ModuleRegistry.CreateDefault(); _journal = new();
    _runtime = new(registry, new(registry), _journal);
    await _runtime.StartAsync(default);
  }
  [TearDown]
  public async Task Cleanup() { await _runtime.DisposeAsync(); Directory.Delete(_directory, true); }
  private Task<ApplyResult> Apply(FlowDocument graph, long revision = 1) =>
    _runtime.ApplyAsync(_documentId, Path.Combine(_directory, "flow.json"), revision, graph);
  private Task<CommandResult> Send(Punch? punch = null)
  {
    var s = _runtime.Snapshot();
    return _runtime.ExecuteCommandAsync(s.SessionId, s.Revision, "manual", "send",
      JsonSerializer.SerializeToNode(new { punch = punch ?? Event() }, FlowJson.Options)!.AsObject());
  }
  private static async Task WaitUntil(Func<bool> predicate)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!predicate()) await Task.Delay(10, timeout.Token);
  }

  [Test]
  public async Task Deduplicate_CancellationRestorationAndReplay_DoNotCorruptLiveState()
  {
    var graph = new FlowDocument
    {
      Nodes = [Node("manual", "source.manual"), Node("dedup", "processor.deduplicate"), Node("after", "processor.passthrough")],
      Edges = [Edge("one", "manual", "dedup"), Edge("two", "dedup", "after")]
    };
    await Apply(graph);
    await Send(); await Send(); await Send(Event(true)); await Send(Event(true));
    Assert.That(_journal.Read("after", "output").Items.Select(i => i.Punch.Cancellation), Is.EqualTo(new[] { false, true }));
    var original = _journal.Read("manual", "output").Items.First();
    var state = _runtime.Snapshot();
    await _runtime.ReplayAsync("manual", new(state.SessionId, state.Revision, Guid.NewGuid(), [original.Id]));
    await Send(Event(true)); // Replay must not restore the live cancelled state.
    Assert.That(_journal.Read("after", "output").Items.Count, Is.EqualTo(3));
    await Send(); await Send(); await Send(Event(true));
    Assert.That(_journal.Read("after", "output").Items.Select(i => i.Punch.Cancellation), Is.EqualTo(new[] { false, true, false, false, true }));
    var applied = await Apply(graph, 2);
    Assert.That(applied.Restarted, Is.Empty);
    await Send(Event(true));
    Assert.That(_journal.Read("after", "output").Items.Count, Is.EqualTo(5), "Unchanged instances retain deduplication state across Apply.");
    Assert.That(_journal.Read("dedup", "processing").Items.All(i => i.Status == "Suppressed"), Is.True);
  }

  [Test]
  public async Task Deduplicate_HistoryIsBoundedAndExpires_AndStatesHaveSeparateIdentities()
  {
    await using var module = new DeduplicateModule(new() { Capacity = 1, RetentionSeconds = 1 });
    var one = Event(); var two = one with { CompetitorId = "5678" };
    Assert.That(module.Process(one, false), Is.Not.Null);
    Assert.That(module.Process(one, false), Is.Null);
    module.Process(two, false);
    Assert.That(module.Process(one, false), Is.Not.Null, "Capacity evicts the oldest identity.");
    await Task.Delay(1050);
    Assert.That(module.Process(one, false), Is.Not.Null, "Age evicts remembered identities.");
    Assert.That(module.Process(one with { CompetitorStatus = CompetitorStatus.DNS }, false), Is.Not.Null);
  }

  [Test]
  public async Task SharedProvider_RefreshesOnce_EnrichesBothBranches_AndEmitsStatusChanges()
  {
    var version = 0;
    var fetches = 0;
    await using var server = new LocalHttp(async context =>
    {
      if (context.Request.RawUrl!.StartsWith("/ORServer.fullweb"))
      {
        Interlocked.Increment(ref fetches);
        return JsonSerializer.Serialize(new
        {
          update = "2026-09-06T10:00:00Z", race = new { startutc = "2026-09-06T08:00:00Z" },
          competitors = new[] { new { bib = 101, card = 1234, name = version == 0 ? "Alex" : "Updated", surname = "Runner", status = version == 0 ? "GA" : "NP" } }
        });
      }
      await Task.Delay(200);
      return "{\"update\":\"stable\"}";
    });
    var graph = new FlowDocument
    {
      Nodes = [Node("manual", "source.manual"), Node("provider", "provider.oribos", new OribosProviderSettings { Host = server.Url, EmitStatusChanges = true }),
        Node("left", "processor.enrichment", new EnrichmentSettings { ProviderId = "provider" }),
        Node("right", "processor.enrichment", new EnrichmentSettings { ProviderId = "provider" }), Node("status", "processor.passthrough")],
      Edges = [Edge("left", "manual", "left"), Edge("right", "manual", "right"), Edge("status", "provider", "status")]
    };
    await Apply(graph);
    await WaitUntil(() => _runtime.Snapshot().Nodes.Single(n => n.Id == "provider").Status == "Running");
    await Task.Delay(350); // Allow the initial update marker to settle.
    var initialFetches = fetches;
    await Send();
    Assert.That(_journal.Read("left", "output").Items.Single().Punch.Competitor?.Bib, Is.EqualTo("101"));
    Assert.That(_journal.Read("right", "output").Items.Single().Punch.Competitor?.Name, Is.EqualTo("Alex Runner"));
    Assert.That(_journal.Read("status", "output").Items, Is.Empty, "Initial snapshots do not emit historic statuses.");
    version = 1;
    var state = _runtime.Snapshot();
    await _runtime.ExecuteCommandAsync(state.SessionId, state.Revision, "provider", "refresh", new());
    await WaitUntil(() => _journal.Read("status", "output").Items.Count == 1);
    await Send();
    Assert.That(fetches, Is.EqualTo(initialFetches + 1), "Two enrichment processors share one refresh.");
    Assert.That(_journal.Read("right", "output").Items.Last().Punch.Competitor?.Name, Is.EqualTo("Updated Runner"));
    Assert.That(_journal.Read("status", "output").Items.Single().Punch.CompetitorStatus, Is.EqualTo(CompetitorStatus.DNS));
    await Apply(graph, 2);
    Assert.That(fetches, Is.EqualTo(initialFetches + 1), "An unchanged provider is kept.");
    await _runtime.StopFlowAsync();
    var stopped = fetches;
    await Task.Delay(300);
    Assert.That(fetches, Is.EqualTo(stopped), "Retired providers stop polling.");
    Assert.ThrowsAsync<FlowException>(async () => await _runtime.ViewAsync(state.SessionId, state.Revision, "provider"));
  }

  [Test]
  public void ProviderReferencesAndTypedProtocolSettings_AreValidatedBeforeStarting()
  {
    var registry = ModuleRegistry.CreateDefault(); var validator = new FlowValidator(registry);
    var graph = new FlowDocument { Nodes = [Node("enrich", "processor.enrichment", new { providerId = "missing" })] };
    Assert.That(validator.Validate(graph).Any(i => i.Field == "settings.providerId"), Is.True);
    foreach (var type in new[] { "source.sirap", "source.microplus", "source.obr", "target.sirap" })
      Assert.That(validator.Validate(new() { Nodes = [Node("bad", type, new { port = -1 })] }), Is.Not.Empty);
    Assert.That(registry.Catalog, Has.Count.EqualTo(22));
    foreach (var definition in registry.Catalog)
    {
      Assert.That(definition.Fields.Any(f => f.Key is "filter" or "enable" or "useStartNumbers"), Is.False);
      Assert.That(definition.Defaults.Select(kv => kv.Key), Is.EquivalentTo(definition.Fields.Select(f => f.Key)));
    }
    Assert.That(registry.Find("source.mqtt")!.Descriptor.Fields.Single(f => f.Key == "protocols").Choices, Does.Contain("Sportident"));
  }

  [Test]
  public async Task PendingHttpRetries_CompleteAtOldDestinationBeforeApply_WithoutGlobalJobs()
  {
    var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var oldCount = 0; var newCount = 0;
    await using var oldServer = new LocalHttp(async context =>
    {
      var attempt = Interlocked.Increment(ref oldCount);
      arrived.TrySetResult();
      if (attempt == 1) { context.Response.StatusCode = 503; return "Busy"; }
      await release.Task;
      return "OK";
    });
    await using var newServer = new LocalHttp(_ => { Interlocked.Increment(ref newCount); return Task.FromResult("OK"); });
    FlowDocument Graph(string url) => new()
    {
      Nodes = [Node("manual", "source.manual"), Node("http", "target.http", new HttpSettings { Url = url })],
      Edges = [Edge("send", "manual", "http")]
    };
    await Apply(Graph(oldServer.Url));
    try
    {
      await Send(); await Send();
      await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
      var apply = Apply(Graph(newServer.Url), 2);
      await Task.Delay(100);
      Assert.That(apply.IsCompleted, Is.False, "Apply waits for accepted deliveries.");
      release.SetResult();
      await apply;
      await Send(); await _runtime.StopFlowAsync();
      Assert.That(oldCount, Is.EqualTo(3)); Assert.That(newCount, Is.EqualTo(1));
      Assert.That(_journal.Read("http", "delivery").Items.Select(i => i.Revision), Is.EqualTo(new long[] { 1, 1, 2 }));
    }
    finally { release.TrySetResult(); }
  }

  [TestCase("source.microplus")]
  [TestCase("source.obr")]
  [TestCase("source.sirap")]
  public async Task ProtocolListener_CanBeRemovedAndRecreatedOnTheSamePort(string type)
  {
    using var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    var graph = new FlowDocument { Nodes = [Node("receiver", type, new { port })] };
    for (var i = 0; i < 3; i++)
    {
      await Apply(graph, i * 2 + 1);
      await Apply(new(), i * 2 + 2);
    }
  }

  [Test]
  public async Task ComplexExample_ProducesTheExpectedRawAndDeduplicatedFiles()
  {
    var examples = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../examples"));
    var graph = FlowJson.Read(await File.ReadAllTextAsync(Path.Combine(examples, "dedup-branches.radiosender.json")));
    using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(examples, "fixtures/dedup-events.json")));
    await Apply(graph);
    foreach (var cancelled in fixture.RootElement.GetProperty("cancellations").EnumerateArray()) await Send(Event(cancelled.GetBoolean()));
    // Apply drains delayed branches; Stop intentionally cancels events still waiting on a delay.
    await Apply(graph, 2);
    await _runtime.StopFlowAsync();
    Assert.That(await File.ReadAllLinesAsync(Path.Combine(_directory, "finish.csv")),
      Is.EqualTo(fixture.RootElement.GetProperty("expectedFinish").EnumerateArray().Select(x => x.GetString())));
    Assert.That(await File.ReadAllLinesAsync(Path.Combine(_directory, "raw.csv")),
      Is.EqualTo(fixture.RootElement.GetProperty("expectedRaw").EnumerateArray().Select(x => x.GetString())));
  }

  [Test]
  public async Task ObrAdapter_PublishesEveryEvent_UntilAnExplicitDeduplicationNode()
  {
    using var probe = new UdpClient(0);
    var port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port; probe.Close();
    var graph = new FlowDocument
    {
      Nodes = [Node("obr", "source.obr", new { port }), Node("dedup", "processor.deduplicate")],
      Edges = [Edge("data", "obr", "dedup")]
    };
    await Apply(graph);
    using var sender = new UdpClient();
    var payload = Encoding.UTF8.GetBytes("SI:1234|CP:35|TM:123000");
    await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, port));
    await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, port));
    await WaitUntil(() => _journal.Read("obr", "output").Items.Count == 2);
    Assert.That(_journal.Read("dedup", "output").Items, Has.Count.EqualTo(1));
  }

  [Test]
  public async Task ProtocolHttpTargets_UseExistingEncoders_AndReportUnsupportedEvents()
  {
    var paths = new ConcurrentQueue<string>(); var bodies = new ConcurrentQueue<string>();
    await using var server = new LocalHttp(async context =>
    {
      paths.Enqueue(context.Request.RawUrl!);
      using var reader = new StreamReader(context.Request.InputStream);
      bodies.Enqueue(await reader.ReadToEndAsync());
      return "<html><body><h1>Ok</h1></body></html>";
    });
    var graph = new FlowDocument
    {
      Nodes = [Node("manual", "source.manual"), Node("oribos", "target.oribos", new { host = server.Url }),
        Node("oresults", "target.oresults", new { host = server.Url, apiKey = "test-key" })],
      Edges = [Edge("oribos", "manual", "oribos"), Edge("oresults", "manual", "oresults")]
    };
    await Apply(graph);
    await Send(); await Send(Event(true));
    await _runtime.StopFlowAsync();
    Assert.That(paths, Has.Count.EqualTo(2), "Unsupported card cancellation is not sent as an ordinary punch.");
    Assert.That(paths.Any(p => p.StartsWith("/radiotime.html?card=1234")), Is.True);
    using var body = JsonDocument.Parse(bodies.Single(b => b.Length > 0));
    Assert.That(body.RootElement.GetProperty("api_key").GetString(), Is.EqualTo("test-key"));
    Assert.That(body.RootElement.GetProperty("records")[0].GetProperty("card").GetInt32(), Is.EqualTo(1234));
    Assert.That(_journal.Read("oribos", "delivery").Items.Select(i => i.Status), Is.EqualTo(new[] { "Accepted", "Suppressed" }));
    Assert.That(_journal.Read("oresults", "delivery").Items.Select(i => i.Status), Is.EqualTo(new[] { "Accepted", "Suppressed" }));
  }

  [TestCase("target.http")]
  [TestCase("target.oribos")]
  [TestCase("target.oresults")]
  public async Task HttpRetries_RecoverWithTheSamePayload_AndRecordOneDelivery(string type)
  {
    var paths = new ConcurrentQueue<string>();
    var bodies = new ConcurrentQueue<string>();
    var count = 0;
    await using var server = new LocalHttp(async context =>
    {
      paths.Enqueue(context.Request.RawUrl!);
      using var reader = new StreamReader(context.Request.InputStream);
      bodies.Enqueue(await reader.ReadToEndAsync());
      context.Response.StatusCode = Interlocked.Increment(ref count) <= 2 ? 503 : 200;
      return "Ok";
    });
    object settings = type == "target.http" ? new HttpSettings { Url = server.Url + "?card={CompetitorId}" } :
      new { host = server.Url, apiKey = "test-key" };
    if (type == "target.oribos") settings = new OribosTargetSettings { Host = server.Url };
    await Apply(new()
    {
      Nodes = [Node("manual", "source.manual"), Node("http", type, settings)],
      Edges = [Edge("send", "manual", "http")]
    });
    await Send();
    await WaitUntil(() => _journal.Read("http", "delivery").Items.Count == 1);
    var delivery = _journal.Read("http", "delivery").Items.Single();
    Assert.That(count, Is.EqualTo(3));
    Assert.That(paths.Distinct().Count(), Is.EqualTo(1));
    Assert.That(bodies.Distinct().Count(), Is.EqualTo(1), "Every retry recreates the same request body.");
    if (type == "target.oresults") Assert.That(bodies.First(), Does.Contain("test-key"));
    Assert.That(delivery.Status, Is.EqualTo("Accepted"));
    Assert.That(delivery.Detail, Does.Contain("3 attempts"));
  }

  [TestCase(408, 4)]
  [TestCase(429, 4)]
  [TestCase(500, 4)]
  [TestCase(503, 4)]
  [TestCase(400, 1)]
  [TestCase(401, 1)]
  [TestCase(404, 1)]
  public async Task HttpRetries_StopAfterTheLimit_AndManualRetryOnlySendsToThatTarget(int status, int attempts)
  {
    var count = 0;
    var healthy = false;
    await using var server = new LocalHttp(context =>
    {
      Interlocked.Increment(ref count);
      context.Response.StatusCode = Volatile.Read(ref healthy) ? 200 : status;
      return Task.FromResult("Ok");
    });
    await Apply(new()
    {
      Nodes = [Node("manual", "source.manual"), Node("http", "target.http", new HttpSettings { Url = server.Url }),
        Node("other", "processor.passthrough")],
      Edges = [Edge("send", "manual", "http"), Edge("other", "manual", "other")]
    });
    await Send();
    Assert.That(_journal.Read("other", "output").Items, Has.Count.EqualTo(1), "Retries must not block another branch.");
    await WaitUntil(() => _journal.Read("http", "delivery").Items.Count == 1);
    var delivery = _journal.Read("http", "delivery").Items.Single();
    Assert.That(count, Is.EqualTo(attempts));
    Assert.That(delivery.Status, Is.EqualTo("Failed"));
    Assert.That(delivery.Detail, Does.Contain($"HTTP {status}").And.Contain($"{attempts} attempt"));
    var input = _journal.Read("http", "input").Items.Single();
    Volatile.Write(ref healthy, true);
    var state = _runtime.Snapshot();
    await _runtime.ReplayAsync("http", new(state.SessionId, state.Revision, Guid.NewGuid(), [input.Id], "in"), retry: true);
    await WaitUntil(() => _journal.Read("http", "delivery").Items.Count == 2);
    Assert.That(_journal.Read("http", "delivery").Items.Last().Status, Is.EqualTo("Accepted"));
    Assert.That(count, Is.EqualTo(attempts + 1));
    Assert.That(_journal.Read("other", "output").Items, Has.Count.EqualTo(1));
  }

  [Test]
  public async Task HttpRetries_RespectRetryAfterBeforeRecovering()
  {
    var count = 0;
    var elapsed = new System.Diagnostics.Stopwatch();
    await using var server = new LocalHttp(context =>
    {
      if (Interlocked.Increment(ref count) == 1)
      {
        elapsed.Start();
        context.Response.StatusCode = 429;
        context.Response.Headers["Retry-After"] = "1";
      }
      else elapsed.Stop();
      return Task.FromResult("Ok");
    });
    await using var module = HttpDeliveryModule.Http(new() { Url = server.Url });
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var result = await module.SendAsync(Event(), timeout.Token);
    Assert.That(result.Status, Is.EqualTo("Accepted"));
    Assert.That(count, Is.EqualTo(2));
    Assert.That(elapsed.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950)));
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task HttpRetries_CancelDuringRetryAfter_WithoutSendingAgain(bool useDate)
  {
    var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var count = 0;
    await using var server = new LocalHttp(context =>
    {
      Interlocked.Increment(ref count);
      context.Response.StatusCode = 503;
      context.Response.Headers["Retry-After"] = useDate ? DateTimeOffset.UtcNow.AddMinutes(1).ToString("r") : "60";
      arrived.TrySetResult();
      return Task.FromResult("Busy");
    });
    await using var module = HttpDeliveryModule.Http(new() { Url = server.Url });
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var send = module.SendAsync(Event(), cancellation.Token).AsTask();
    await arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    // Let the response be consumed and enter the backoff before cancelling.
    await Task.Delay(100);
    await cancellation.CancelAsync();
    Assert.CatchAsync<OperationCanceledException>(async () => await send.WaitAsync(TimeSpan.FromSeconds(2)));
    Assert.That(count, Is.EqualTo(1));
  }

  [Test]
  public async Task HttpRetries_LongRetryAfter_StillRespectsTheDeliveryDeadline()
  {
    var count = 0;
    await using var server = new LocalHttp(context =>
    {
      Interlocked.Increment(ref count);
      context.Response.StatusCode = 503;
      context.Response.Headers["Retry-After"] = "60";
      return Task.FromResult("Busy");
    });
    await Apply(new()
    {
      Nodes = [Node("manual", "source.manual"), Node("http", "target.http", new HttpSettings { Url = server.Url })],
      Edges = [Edge("send", "manual", "http")]
    });
    await Send();
    await WaitUntil(() => _journal.Read("http", "delivery").Items.Count == 1);
    var delivery = _journal.Read("http", "delivery").Items.Single();
    Assert.That(delivery.Status, Is.EqualTo("Failed"));
    Assert.That(delivery.Detail, Does.Contain("five-second deadline"));
    Assert.That(count, Is.EqualTo(1));
  }

  [Test]
  public async Task HttpRetries_MissingOribosAcknowledgement_IsNotRetried()
  {
    var count = 0;
    await using var server = new LocalHttp(_ => { Interlocked.Increment(ref count); return Task.FromResult("Rejected"); });
    await using var module = HttpDeliveryModule.Oribos(new() { Host = server.Url });
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var result = await module.SendAsync(Event(), timeout.Token);
    Assert.That(result.Status, Is.EqualTo("Failed"));
    Assert.That(result.Detail, Does.Contain("did not acknowledge"));
    Assert.That(count, Is.EqualTo(1));
  }

  [Test]
  public async Task HttpRetries_ConnectionRefused_StopsAfterFourAttempts()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    await using var module = HttpDeliveryModule.Http(new() { Url = $"http://127.0.0.1:{port}/" });
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var result = await module.SendAsync(Event(), timeout.Token);
    Assert.That(result.Status, Is.EqualTo("Failed"));
    Assert.That(result.Detail, Does.Contain("HTTP transport error").And.Contain("4 attempts"));
  }

  // Each handler is tracked and joined; no external service or credentials are needed.
  private sealed class LocalHttp : IAsyncDisposable
  {
    private readonly HttpListener _listener = new();
    private readonly ConcurrentBag<Task> _requests = [];
    private readonly Task _loop;
    public string Url { get; }
    public LocalHttp(Func<HttpListenerContext, Task<string>> handler)
    {
      using var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
      Url = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/"; probe.Stop();
      _listener.Prefixes.Add(Url); _listener.Start();
      _loop = Task.Run(async () =>
      {
        try
        {
          while (_listener.IsListening)
          {
            var context = await _listener.GetContextAsync();
            _requests.Add(Reply(context, handler));
          }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
      });
    }
    private static async Task Reply(HttpListenerContext context, Func<HttpListenerContext, Task<string>> handler)
    {
      try
      {
        var bytes = Encoding.UTF8.GetBytes(await handler(context));
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
      }
      catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException) { }
      finally { context.Response.Close(); }
    }
    public async ValueTask DisposeAsync() { _listener.Close(); await _loop; await Task.WhenAll(_requests); }
  }
}

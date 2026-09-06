using NUnit.Framework;
using RadioSender.Configuration;
using RadioSender.Flow;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Test.RadioSender;

[TestFixture]
public class TestFlowDocuments
{
  private string _directory = null!;
  [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "radiosender-doc-test-" + Guid.NewGuid()); Directory.CreateDirectory(_directory); }
  [TearDown] public void TearDown() => Directory.Delete(_directory, true);

  [Test]
  public async Task IncompleteDraftRoundTrips_AndSaveAsKeepsOriginal()
  {
    using var documents = new FlowDocuments();
    var original = Path.Combine(_directory, "original.json");
    var copy = Path.Combine(_directory, "copy.json");
    var created = await documents.OpenAsync(original, true);
    created.Document.Nodes.Add(TestFlowRuntime.Node("tcp", "source.tcp", new { port = "" }));
    var saved = await documents.SaveAsync(created.Id, created.Revision, created.Document);
    Assert.That(saved.Revision, Is.GreaterThan(created.Revision));
    var originalBytes = File.ReadAllText(original);
    saved.Document.Nodes[0] = saved.Document.Nodes[0] with { Name = "Changed in copy" };
    var copied = await documents.SaveAsync(saved.Id, saved.Revision, saved.Document, copy);
    Assert.That(File.ReadAllText(original), Is.EqualTo(originalBytes));
    using var reopenedStore = new FlowDocuments();
    var reopened = await reopenedStore.OpenAsync(copy, false);
    Assert.That(reopened.Document.Nodes[0].Name, Is.EqualTo("Changed in copy"));
    Assert.That(reopened.Document.Nodes[0].Settings["port"]!.GetValue<string>(), Is.Empty);
    Assert.That(copied.Path, Is.EqualTo(copy));
  }

  [Test]
  public async Task StaleEditorAndExternalFileChanges_CannotBeOverwritten()
  {
    using var documents = new FlowDocuments();
    var path = Path.Combine(_directory, "flow.json");
    var created = await documents.OpenAsync(path, true);
    created.Document.Nodes.Add(TestFlowRuntime.Node("manual", "source.manual"));
    var updated = await documents.SaveAsync(created.Id, created.Revision, created.Document);
    Assert.ThrowsAsync<FlowException>(async () => await documents.SaveAsync(created.Id, created.Revision, new()));
    var outside = File.ReadAllText(path).Replace("manual", "external");
    await File.WriteAllTextAsync(path, outside);
    Assert.ThrowsAsync<FlowException>(async () => await documents.SaveAsync(updated.Id, updated.Revision, new()));
    Assert.That(File.ReadAllText(path), Is.EqualTo(outside));
    var reloaded = await documents.OpenAsync(path, false);
    Assert.That(reloaded.Revision, Is.GreaterThan(updated.Revision));
  }

  [Test]
  public async Task FailedSaveAs_PreservesCurrentDocumentAndExistingDestination()
  {
    using var documents = new FlowDocuments();
    var created = await documents.OpenAsync(Path.Combine(_directory, "flow.json"), true);
    var destination = Path.Combine(_directory, "existing.json");
    await File.WriteAllTextAsync(destination, "keep me");
    Assert.ThrowsAsync<FlowException>(async () => await documents.SaveAsync(created.Id, created.Revision, newPath: destination));
    Assert.That((await documents.ReadAsync(created.Id)).Path, Is.EqualTo(created.Path));
    Assert.That(File.ReadAllText(destination), Is.EqualTo("keep me"));
  }

  [TestCase("{\"Source\":{}}")] [TestCase("{\"schemaVersion\":2}")] [TestCase("{\"schemaVersion\":1,\"nodes\":null}")]
  public void UnsupportedOrMalformedStructure_IsRejected(string json) => Assert.Throws<FlowException>(() => FlowJson.Read(json));
  [Test]
  public async Task StaleSaveAs_RecoversDraftWithoutChangingOtherEditorsSession()
  {
    using var documents = new FlowDocuments();
    var first = await documents.OpenAsync(Path.Combine(_directory, "first.json"), true);
    var other = await documents.ReadAsync(first.Id);
    other.Document.Nodes.Add(TestFlowRuntime.Node("new", "source.manual"));
    var saved = await documents.SaveAsync(other.Id, other.Revision, other.Document);
    first.Document.Nodes.Add(TestFlowRuntime.Node("recovered", "processor.passthrough"));
    var recovered = await documents.SaveAsync(first.Id, first.Revision, first.Document, Path.Combine(_directory, "recovered.json"));
    Assert.That(recovered.Id, Is.Not.EqualTo(saved.Id));
    Assert.That((await documents.ReadAsync(saved.Id)).Document.Nodes[0].Id, Is.EqualTo("new"));
    Assert.That(recovered.Document.Nodes[0].Id, Is.EqualTo("recovered"));
  }

  [Test]
  public void UnknownEdgeSettings_CannotSilentlyDiscardRoutingRules()
  {
    Assert.Throws<System.Text.Json.JsonException>(() => FlowJson.Read("""
      {"schemaVersion":1,"edges":[{"id":"edge","from":{"node":"a","port":"out"},"to":{"node":"b","port":"in"},"filter":{"mapControls":{"35":1}}}]}
      """));
  }

}

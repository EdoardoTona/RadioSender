using NUnit.Framework;
using RadioSender;
using RadioSender.Runtime;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Test.RadioSender;

[TestFixture, NonParallelizable]
public class TestFlowLogs
{
  [Test]
  public async Task Logs_AreScopedByNodeAndSession_WithIndependentGeneralHistory()
  {
    var sink = new EventLogSink(null);
    using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
    var logs = new FlowLogs();
    await logs.StartAsync(default);
    try
    {
      logger.Information("General event");
      var session = Guid.NewGuid().ToString();
      logger.ForContext("NodeId", "source").ForContext("SessionId", session).Information("Source event");
      logger.ForContext("NodeId", "target").ForContext("SessionId", session).Warning("Target event");
      logger.ForContext("NodeId", "source").ForContext("SessionId", "old-session").Information("Old event");
      Assert.That(logs.Read("general", null, null, 0).Items.Select(e => e.Log.Message), Is.EqualTo(new[] { "General event" }));
      var source = logs.Read("node", "source", session, 0);
      Assert.That(source.Items.Select(e => e.Log.Message), Is.EqualTo(new[] { "Source event" }));
      Assert.That(logs.Read("all", null, null, 0).Items, Has.Count.EqualTo(4));
      Assert.That(logs.Read("node", "source", session, source.Cursor).Items, Is.Empty);
    }
    finally { await logs.StopAsync(default); }
  }
}

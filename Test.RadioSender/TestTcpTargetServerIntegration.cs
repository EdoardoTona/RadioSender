using RadioSender.Flow.Modules;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using RadioSender.Hosts.Common;

namespace Test.RadioSender;

// End-to-end: The TCP target listens; a plain socket connects in as a client and reads the
// raw bytes the server pushes, exercising the Cancellation/Status format-suppression logic.
[TestFixture]
public class TestTcpTargetServerIntegration
{
  private static int FreePort()
  {
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    var port = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return port;
  }

  private static Punch Punch(bool cancellation = false, CompetitorStatus status = CompetitorStatus.Unknown) => new(
    CompetitorId: "7",
    Control: 90,
    SourceId: "test",
    ReceivedAt: DateTimeOffset.UtcNow,
    Time: new DateTime(2026, 07, 24, 10, 1, 3, 880),
    Cancellation: cancellation,
    CompetitorStatus: status);

  private static async Task<(TcpFlowModule server, NetworkStream stream, TcpClient connected)> StartAndConnect(string format)
  {
    var port = FreePort();
    var config = new TcpSettings { Port = port, Format = format, AsServer = true };
    var server = new TcpFlowModule(config, new("tcp", System.IO.Path.GetTempPath(), new CapturingOutput()), false);
    await server.StartAsync(default);

    var connected = new TcpClient();
    await connected.ConnectAsync(IPAddress.Loopback, port);
    await Task.Delay(100); // let the server register the session

    return (server, connected.GetStream(), connected);
  }

  private static async Task<string> ReadAvailable(NetworkStream stream, int timeoutMs = 1000)
  {
    var buffer = new byte[4096];
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!stream.DataAvailable && sw.ElapsedMilliseconds < timeoutMs)
      await Task.Delay(20);

    if (!stream.DataAvailable)
      return "";

    var read = await stream.ReadAsync(buffer);
    return Encoding.UTF8.GetString(buffer, 0, read);
  }

  [Test]
  public async Task FormatWithoutCancellationPlaceholder_CancellationPunch_IsSuppressed()
  {
    var (server, stream, connected) = await StartAndConnect("{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}");
    try
    {
      await server.SendAsync(Punch(cancellation: true), default);
      var received = await ReadAvailable(stream, 500);

      Assert.That(received, Is.Empty);
    }
    finally
    {
      connected.Dispose();
      await server.DisposeAsync();
    }
  }

  [Test]
  public async Task FormatWithNeitherStatusNorTimePlaceholder_StatusPunch_IsSuppressed()
  {
    var (server, stream, connected) = await StartAndConnect("{CompetitorId};{Control}{CRLF}");
    try
    {
      await server.SendAsync(Punch(status: CompetitorStatus.DNS), default);
      var received = await ReadAvailable(stream, 500);

      Assert.That(received, Is.Empty);
    }
    finally
    {
      connected.Dispose();
      await server.DisposeAsync();
    }
  }

  [Test]
  public async Task FormatWithTimeButNoStatusPlaceholder_StatusPunch_UsesSentinelTime()
  {
    var (server, stream, connected) = await StartAndConnect("{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}");
    try
    {
      await server.SendAsync(Punch(status: CompetitorStatus.DNS), default);
      var received = await ReadAvailable(stream);

      Assert.That(received, Does.Contain("00:00:01"));
    }
    finally
    {
      connected.Dispose();
      await server.DisposeAsync();
    }
  }

  [Test]
  public async Task FormatWithExplicitStatusPlaceholder_UsesRealTimeNotSentinel()
  {
    var (server, stream, connected) = await StartAndConnect("{CompetitorId};{Control};{Time:HH:mm:ss,fff};{Status}{CRLF}");
    try
    {
      await server.SendAsync(Punch(status: CompetitorStatus.DNS), default);
      var received = await ReadAvailable(stream);

      Assert.That(received, Does.Contain("10:01:03,880"));
      Assert.That(received, Does.Contain("DNS"));
      Assert.That(received, Does.Not.Contain("00:00:01"));
    }
    finally
    {
      connected.Dispose();
      await server.DisposeAsync();
    }
  }
}

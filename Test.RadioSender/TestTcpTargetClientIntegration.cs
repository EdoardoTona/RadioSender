using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Enrichment;
using RadioSender.Hosts.Target.Tcp;

namespace Test.RadioSender;

// End-to-end: TcpTargetClient connects out to a real loopback listener; asserts on the raw
// bytes it writes, so it also exercises the Cancellation/Status format-suppression logic.
[TestFixture]
public class TestTcpTargetClientIntegration
{
  private sealed class StubMonitor(FiltersConfiguration value) : IOptionsMonitor<FiltersConfiguration>
  {
    public FiltersConfiguration CurrentValue => value;
    public FiltersConfiguration Get(string? name) => value;
    public IDisposable? OnChange(Action<FiltersConfiguration, string?> listener) => null;
  }

  private static FilterService BuildFilterService()
    => new(new StubMonitor(new FiltersConfiguration { List = [] }), Array.Empty<IEnrichmentSource>());

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

  // Connects a TcpTargetClient to `listener`, accepts the connection, and returns the
  // accepted socket's stream to read whatever bytes the client sends.
  private static async Task<(TcpTargetClient client, NetworkStream stream, TcpClient accepted)> Connect(TcpListener listener, string format)
  {
    var config = new TcpTargetConfiguration { Address = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Format = format, AsServer = false };
    var client = new TcpTargetClient(BuildFilterService(), config);

    var accepted = await listener.AcceptTcpClientAsync();
    // Give NetCoreServer's async connect a brief moment to flip IsConnected.
    await Task.Delay(100);

    return (client, accepted.GetStream(), accepted);
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
    var listener = new TcpListener(IPAddress.Loopback, FreePort());
    listener.Start();
    try
    {
      var (client, stream, accepted) = await Connect(listener, "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}");
      try
      {
        await client.SendDispatch(new PunchDispatch(Punches: [Punch(cancellation: true)]));
        var received = await ReadAvailable(stream, 500);

        Assert.That(received, Is.Empty);
      }
      finally
      {
        client.Dispose();
        accepted.Dispose();
      }
    }
    finally
    {
      listener.Stop();
    }
  }

  [Test]
  public async Task FormatWithCancellationPlaceholder_CancellationPunch_IsSent()
  {
    var listener = new TcpListener(IPAddress.Loopback, FreePort());
    listener.Start();
    try
    {
      var (client, stream, accepted) = await Connect(listener, "{CompetitorId};{Control};{Time:HH:mm:ss,fff};{Cancellation}{CRLF}");
      try
      {
        await client.SendDispatch(new PunchDispatch(Punches: [Punch(cancellation: true)]));
        var received = await ReadAvailable(stream);

        Assert.That(received, Does.Contain("ANN"));
      }
      finally
      {
        client.Dispose();
        accepted.Dispose();
      }
    }
    finally
    {
      listener.Stop();
    }
  }

  [Test]
  public async Task FormatWithTimeButNoStatusPlaceholder_StatusPunch_UsesSentinelTime()
  {
    // {Status} isn't in the format, but {Time} is — status must still get through, encoded
    // as the sentinel time, exactly as documented for the Tcp target.
    var listener = new TcpListener(IPAddress.Loopback, FreePort());
    listener.Start();
    try
    {
      var (client, stream, accepted) = await Connect(listener, "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}");
      try
      {
        await client.SendDispatch(new PunchDispatch(Punches: [Punch(status: CompetitorStatus.DNS)]));
        var received = await ReadAvailable(stream);

        Assert.That(received, Does.Contain("00:00:01"));
      }
      finally
      {
        client.Dispose();
        accepted.Dispose();
      }
    }
    finally
    {
      listener.Stop();
    }
  }

  [Test]
  public async Task FormatWithNeitherStatusNorTimePlaceholder_StatusPunch_IsSuppressed()
  {
    var listener = new TcpListener(IPAddress.Loopback, FreePort());
    listener.Start();
    try
    {
      var (client, stream, accepted) = await Connect(listener, "{CompetitorId};{Control}{CRLF}");
      try
      {
        await client.SendDispatch(new PunchDispatch(Punches: [Punch(status: CompetitorStatus.DNS)]));
        var received = await ReadAvailable(stream, 500);

        Assert.That(received, Is.Empty);
      }
      finally
      {
        client.Dispose();
        accepted.Dispose();
      }
    }
    finally
    {
      listener.Stop();
    }
  }

  [Test]
  public async Task FormatWithExplicitStatusPlaceholder_UsesRealTimeNotSentinel()
  {
    // {Status} is present explicitly, so the real {Time} must be preserved instead of
    // being clobbered by the sentinel encoding (which is only a fallback for formats
    // without {Status}).
    var listener = new TcpListener(IPAddress.Loopback, FreePort());
    listener.Start();
    try
    {
      var (client, stream, accepted) = await Connect(listener, "{CompetitorId};{Control};{Time:HH:mm:ss,fff};{Status}{CRLF}");
      try
      {
        await client.SendDispatch(new PunchDispatch(Punches: [Punch(status: CompetitorStatus.DNS)]));
        var received = await ReadAvailable(stream);

        Assert.That(received, Does.Contain("10:01:03,880"));
        Assert.That(received, Does.Contain("DNS"));
        Assert.That(received, Does.Not.Contain("00:00:01"));
      }
      finally
      {
        client.Dispose();
        accepted.Dispose();
      }
    }
    finally
    {
      listener.Stop();
    }
  }
}

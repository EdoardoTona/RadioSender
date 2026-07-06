using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Enrichment;
using RadioSender.Hosts.Source.Tcp;
using RadioSender.Hosts.Target;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Test.RadioSender;

// End-to-end: real TCP socket -> TcpSourceServer -> line reader -> FilterService ->
// DispatcherService -> capturing target. Exercises the runtime path, not just the parser.
[TestFixture]
public class TestTcpSourceIntegration
{
  private sealed class StubMonitor(FiltersConfiguration value) : IOptionsMonitor<FiltersConfiguration>
  {
    public FiltersConfiguration CurrentValue => value;
    public FiltersConfiguration Get(string? name) => value;
    public IDisposable? OnChange(Action<FiltersConfiguration, string?> listener) => null;
  }

  private sealed class CapturingTarget : ITarget
  {
    public ConcurrentQueue<Punch> Received { get; } = new();

    public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      if (dispatch.Punches != null)
        foreach (var p in dispatch.Punches)
          Received.Enqueue(p);
      return Task.CompletedTask;
    }

    public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var d in dispatches)
        SendDispatch(d, ct);
      return Task.CompletedTask;
    }
  }

  private static int FreePort()
  {
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    var port = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return port;
  }

  private static (TcpSourceServer server, CapturingTarget target) BuildServer(int port, string format)
  {
    var filterService = new FilterService(
      new StubMonitor(new FiltersConfiguration { List = [] }),
      Array.Empty<IEnrichmentSource>());

    var target = new CapturingTarget();
    var dispatcher = new DispatcherService(filterService, new ITarget[] { target }, new DispatcherConfiguration());

    var config = new TcpSourceConfiguration { Port = port, Format = format, AsServer = true, SourceId = "tcp-test" };
    var server = new TcpSourceServer(filterService, dispatcher, config);
    return (server, target);
  }

  private static async Task<Punch> WaitForOne(CapturingTarget target, int timeoutMs = 3000)
  {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs)
    {
      if (target.Received.TryDequeue(out var p))
        return p;
      await Task.Delay(20);
    }
    throw new TimeoutException("No punch dispatched within timeout");
  }

  [Test]
  public async Task ReceivesPunchOverRealSocket_DefaultFormat()
  {
    var port = FreePort();
    var (server, target) = BuildServer(port, ConfigureTcpSource.DefaultFormat);
    await server.StartAsync(CancellationToken.None);

    try
    {
      using var client = new System.Net.Sockets.TcpClient();
      await client.ConnectAsync(IPAddress.Loopback, port);
      var stream = client.GetStream();

      var payload = Encoding.UTF8.GetBytes("1234;31;21:45:59,123\r\n");
      await stream.WriteAsync(payload);
      await stream.FlushAsync();

      var punch = await WaitForOne(target);

      Assert.Multiple(() =>
      {
        Assert.That(punch.CompetitorId, Is.EqualTo("1234"));
        Assert.That(punch.Control, Is.EqualTo(31));
        Assert.That(punch.Time.Hour, Is.EqualTo(21));
        Assert.That(punch.Time.Minute, Is.EqualTo(45));
        Assert.That(punch.Time.Second, Is.EqualTo(59));
        Assert.That(punch.Time.Millisecond, Is.EqualTo(123));
        Assert.That(punch.SourceId, Is.EqualTo("tcp-test"));
      });
    }
    finally
    {
      await server.StopAsync(CancellationToken.None);
      server.Dispose();
    }
  }

  [Test]
  public async Task ReassemblesLineSplitAcrossTwoTcpPackets()
  {
    var port = FreePort();
    var (server, target) = BuildServer(port, ConfigureTcpSource.DefaultFormat);
    await server.StartAsync(CancellationToken.None);

    try
    {
      using var client = new System.Net.Sockets.TcpClient();
      await client.ConnectAsync(IPAddress.Loopback, port);
      var stream = client.GetStream();

      // Split the line mid-field across two writes.
      await stream.WriteAsync(Encoding.UTF8.GetBytes("1234;31;21:4"));
      await stream.FlushAsync();
      await Task.Delay(100);
      await stream.WriteAsync(Encoding.UTF8.GetBytes("5:59,123\r\n"));
      await stream.FlushAsync();

      var punch = await WaitForOne(target);
      Assert.That(punch.CompetitorId, Is.EqualTo("1234"));
      Assert.That(punch.Time.Second, Is.EqualTo(59));
      Assert.That(punch.Time.Millisecond, Is.EqualTo(123));
    }
    finally
    {
      await server.StopAsync(CancellationToken.None);
      server.Dispose();
    }
  }

  [Test]
  public async Task DecodesSpecialStatusTimeOverSocket()
  {
    var port = FreePort();
    var (server, target) = BuildServer(port, ConfigureTcpSource.DefaultFormat);
    await server.StartAsync(CancellationToken.None);

    try
    {
      using var client = new System.Net.Sockets.TcpClient();
      await client.ConnectAsync(IPAddress.Loopback, port);
      var stream = client.GetStream();

      // 00:00:02 => DNF, as encoded by the Tcp target.
      await stream.WriteAsync(Encoding.UTF8.GetBytes("1234;9;00:00:02,000\r\n"));
      await stream.FlushAsync();

      var punch = await WaitForOne(target);
      Assert.That(punch.CompetitorStatus, Is.EqualTo(CompetitorStatus.DNF));
    }
    finally
    {
      await server.StopAsync(CancellationToken.None);
      server.Dispose();
    }
  }
}

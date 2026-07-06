using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Enrichment;
using RadioSender.Hosts.Source.Microplus; // MicrogateSource lives in this namespace
using RadioSender.Hosts.Target;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Test.RadioSender;

// Exercises the runtime connection path of MicrogateSource: a real socket stands in
// for the Microgate device, and we observe whether the source connects and issues its
// serial-number / retransmission requests on connect.
[TestFixture]
public class TestMicrogateSourceIntegration
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
      foreach (var d in dispatches) SendDispatch(d, ct);
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

  private static MicrogateSource BuildSource(string address, int port)
  {
    var filterService = new FilterService(
      new StubMonitor(new FiltersConfiguration { List = [] }),
      Array.Empty<IEnrichmentSource>());
    var target = new CapturingTarget();
    var dispatcher = new DispatcherService(filterService, new ITarget[] { target }, new DispatcherConfiguration());
    var config = new MicrogateSourceConfiguration { Address = address, Port = port };
    return new MicrogateSource(filterService, dispatcher, config);
  }

  private sealed class CapturingSink : ILogEventSink
  {
    public ConcurrentQueue<string> Messages { get; } = new();
    public void Emit(LogEvent logEvent) => Messages.Enqueue(logEvent.RenderMessage());
    public bool Any(string substring) => Messages.Any(m => m.Contains(substring, StringComparison.OrdinalIgnoreCase));
    public string Dump() => string.Join("\n", Messages);
  }

  [Test]
  public async Task LogsConnectionLifecycleSoItIsObservable()
  {
    // The reported symptom was "no log output at all". Verify the source now emits a
    // startup/connect log and, when the device is unreachable, makes the retry visible.
    var sink = new CapturingSink();
    var prev = Log.Logger;
    Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

    var port = FreePort(); // nothing will listen here at startup
    var source = BuildSource("127.0.0.1", port);
    try
    {
      await source.StartAsync(CancellationToken.None);
      await Task.Delay(2500); // let the initial connect fail and a retry happen

      Assert.Multiple(() =>
      {
        Assert.That(sink.Any("connecting to"), Is.True,
          $"No 'connecting' startup log was emitted. Captured:\n{sink.Dump()}");
        Assert.That(sink.Any("reconnecting"), Is.True,
          $"Retry was not made observable. Captured:\n{sink.Dump()}");
      });
    }
    finally
    {
      await source.StopAsync(CancellationToken.None);
      Log.Logger = prev;
    }
  }

  [Test]
  public async Task ReconnectsWhenDeviceComesUpAfterStartup()
  {
    // Realistic field scenario: RadioSender starts before the Microgate device is
    // reachable. The source must keep retrying and connect once the device appears.
    var port = FreePort();

    var source = BuildSource("127.0.0.1", port);
    await source.StartAsync(CancellationToken.None);

    // Nothing is listening yet; give the initial connect attempt time to fail.
    await Task.Delay(1500);

    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();

    var gotConnection = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var acceptTask = Task.Run(async () =>
    {
      using var conn = await listener.AcceptTcpClientAsync();
      gotConnection.TrySetResult(true);
      await Task.Delay(500);
    });

    try
    {
      var connected = await Task.WhenAny(gotConnection.Task, Task.Delay(8000)) == gotConnection.Task;
      Assert.That(connected, Is.True,
        "MicrogateSource never reconnected after the device came up — dead-on-arrival if the device isn't reachable at startup");
    }
    finally
    {
      await source.StopAsync(CancellationToken.None);
      listener.Stop();
      try { await acceptTask; } catch { }
    }
  }

  [Test]
  public async Task ConnectsToDeviceAndSendsRequests()
  {
    var port = FreePort();
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();

    var gotConnection = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var totalBytes = 0;
    var acceptTask = Task.Run(async () =>
    {
      using var conn = await listener.AcceptTcpClientAsync();
      gotConnection.TrySetResult(true);
      var stream = conn.GetStream();
      var buf = new byte[4096];
      conn.ReceiveTimeout = 4000;
      var sw = System.Diagnostics.Stopwatch.StartNew();
      while (sw.ElapsedMilliseconds < 4000)
      {
        try
        {
          if (!stream.DataAvailable) { await Task.Delay(50); continue; }
          var n = await stream.ReadAsync(buf);
          if (n <= 0) break;
          totalBytes += n;
        }
        catch { break; }
      }
    });

    var source = BuildSource("127.0.0.1", port);
    try
    {
      await source.StartAsync(CancellationToken.None);

      var connected = await Task.WhenAny(gotConnection.Task, Task.Delay(4000)) == gotConnection.Task;
      Assert.That(connected, Is.True, "MicrogateSource did not open a TCP connection to the device");

      // The source issues AskSerialNumber (~1s) and AskRetransmission (~1.5s) after connect.
      await Task.Delay(2500);
      Assert.That(totalBytes, Is.GreaterThan(0), "MicrogateSource connected but never sent any request bytes");
    }
    finally
    {
      await source.StopAsync(CancellationToken.None);
      listener.Stop();
      try { await acceptTask; } catch { }
    }
  }
}

using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public sealed record TcpSettings
{
  [MaxLength(253), Display(Name = "Address", Description = "Remote host in client mode. Server mode listens on all local interfaces.")]
  public string Address { get; init; } = "127.0.0.1";
  [Range(1, 65535), Display(Name = "Port")]
  public int Port { get; init; } = 10000;
  [Display(Name = "Listen as server")]
  public bool AsServer { get; init; }
  [Required, MaxLength(8192), Display(Name = "Format", Description = "Input is line-delimited UTF-8. Example: {CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}")]
  public string Format { get; init; } = "{CompetitorId};{Control};{Time:HH:mm:ss,fff}{CRLF}";
}

public sealed class TcpFlowModule(TcpSettings settings, ModuleContext context, bool source, Func<Punch, byte[]?>? encoder = null) : FlowModule
{
  private readonly CancellationTokenSource _lifetime = new();
  private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();
  private readonly ConcurrentDictionary<Guid, Task> _sessions = new();
  private TcpListener? _listener;
  private Task? _loop;
  private bool _disposed;

  public override Task StartAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (settings.AsServer)
    {
      _listener = new TcpListener(IPAddress.Any, settings.Port);
      _listener.Server.ExclusiveAddressUse = true;
      _listener.Start();
      SetState("Listening", $"Port {settings.Port}");
      _loop = AcceptAsync(_lifetime.Token);
    }
    else
    {
      SetState("Connecting", $"{settings.Address}:{settings.Port}");
      _loop = ConnectAsync(_lifetime.Token);
    }
    return Task.CompletedTask;
  }

  private async Task AcceptAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var client = await _listener!.AcceptTcpClientAsync(ct);
        if (_clients.Count >= 64)
        { client.Dispose(); SetState("Input warning", "The 64-client connection limit was reached."); continue; }
        client.NoDelay = true;
        var id = Guid.NewGuid();
        _clients[id] = client;
        _sessions[id] = RunSessionAsync(id, client, ct);
        foreach (var done in _sessions.Where(p => p.Value.IsCompleted).ToArray())
          _sessions.TryRemove(done.Key, out _);
      }
    }
    catch (Exception e) when (ct.IsCancellationRequested && e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    catch (Exception e) { SetState("Error", e.Message); }
  }

  private async Task ConnectAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      var client = new TcpClient { NoDelay = true };
      var id = Guid.NewGuid();
      try
      {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(settings.Address, settings.Port, timeout.Token);
        _clients[id] = client;
        SetState("Connected", $"{settings.Address}:{settings.Port}");
        await RunSessionAsync(id, client, ct);
      }
      catch (Exception e) when (e is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
      { if (!ct.IsCancellationRequested) SetState("Disconnected", e.Message); }
      finally { _clients.TryRemove(id, out _); client.Dispose(); }
      if (ct.IsCancellationRequested) break;
      try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
    }
  }

  private async Task RunSessionAsync(Guid id, TcpClient client, CancellationToken ct)
  {
    try
    {
      if (source) await ReadPunchesAsync(client, ct);
      else
      {
        // Read to detect disconnects; a target does not interpret incoming bytes as acknowledgements.
        var buffer = new byte[1024];
        while (await client.GetStream().ReadAsync(buffer, ct) != 0) { }
      }
    }
    catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or FlowException)
    { if (!ct.IsCancellationRequested) SetState("Disconnected", e.Message); }
    finally
    {
      _clients.TryRemove(id, out _);
      client.Dispose();
      if (!ct.IsCancellationRequested)
        SetState(settings.AsServer ? "Listening" : "Disconnected", settings.AsServer ? $"Port {settings.Port}" : "Reconnecting…");
    }
  }

  private async Task ReadPunchesAsync(TcpClient client, CancellationToken ct)
  {
    var parser = FormatStringParser.TryCreate(settings.Format)!;
    using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, true, 4096, leaveOpen: true);
    var buffer = new char[4096];
    var line = new StringBuilder();
    var dropping = false;
    int count;
    while ((count = await reader.ReadAsync(buffer, ct)) != 0)
    {
      for (var i = 0; i < count; i++)
      {
        var ch = buffer[i];
        if (ch is '\r' or '\n')
        {
          if (!dropping && line.Length > 0)
          {
            var punch = parser.TryParse(line.ToString(), context.NodeId);
            if (punch != null) await context.Output.PublishAsync(punch, ct);
            else SetState("Input warning", "A line did not match the input format.");
          }
          line.Clear(); dropping = false;
        }
        else if (!dropping)
        {
          if (line.Length == 8192)
          { line.Clear(); dropping = true; SetState("Input warning", "An input line exceeded 8192 characters and was discarded."); }
          else line.Append(ch);
        }
      }
    }
  }

  public override async ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct)
  {
    string? reason = "This protocol cannot represent the event.";
    var bytes = encoder == null ? FormattedOutput.Encode(punch, settings.Format, true, out reason) : encoder(punch);
    if (bytes == null) return new("Suppressed", reason);
    var clients = _clients.Values.ToArray();
    if (clients.Length == 0) throw new IOException("No TCP receiver is connected. Retry the event after connecting.");
    var results = await Task.WhenAll(clients.Select(async client =>
    {
      try { await client.GetStream().WriteAsync(bytes, ct); return true; }
      catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
      { client.Dispose(); return false; }
    }));
    var sent = results.Count(r => r);
    if (sent == 0) throw new IOException("TCP write failed. The receiver may have received a partial message.");
    return new(sent == clients.Length ? "Written" : "Partial", $"Written to {sent}/{clients.Length} socket(s); receiver acknowledgement is unavailable.");
  }

  public override async Task StopAsync(CancellationToken ct)
  {
    if (_disposed) return;
    await _lifetime.CancelAsync();
    _listener?.Stop();
    foreach (var client in _clients.Values) client.Dispose();
    if (_loop != null) await _loop.WaitAsync(ct);
    await Task.WhenAll(_sessions.Values).WaitAsync(ct);
    _clients.Clear(); _sessions.Clear();
    SetState("Stopped");
  }

  public override async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    await StopAsync(CancellationToken.None);
    _disposed = true;
    _lifetime.Dispose();
  }
}

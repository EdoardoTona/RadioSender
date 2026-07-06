using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.OBR;

public sealed class ObrSource(
  FilterService filterService,
  DispatcherService dispatcherService,
  ObrSourceConfiguration configuration) : ISource, IRadioSenderHost, IDisposable
{
  private readonly CancellationTokenSource _cts = new();
  private UdpClient? _udp;
  private Task? _receiveTask;
  private bool _disposed;

  private string ResolveSourceId(string senderIp) =>
    string.IsNullOrWhiteSpace(configuration.SourceId)
      ? $"OBR:{senderIp}"
      : configuration.SourceId;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    var port = configuration.Port ?? throw new ArgumentException("OBR Port is required", nameof(configuration));

    try
    {
      _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }
    catch (Exception e)
    {
      Log.Error(e, "OBR source unable to bind UDP port {port}", port);
      return Task.CompletedTask;
    }

    Log.Information("OBR source listening on UDP port {port}{filter}",
      port,
      string.IsNullOrWhiteSpace(configuration.AllowedIp) ? "" : $" (only from {configuration.AllowedIp})");

    _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    if (_disposed)
      return;

    _disposed = true;
    _cts.Cancel();

    try { _udp?.Close(); } catch { }

    if (_receiveTask != null)
    {
      try { await _receiveTask; }
      catch (OperationCanceledException) { }
      catch (Exception e) { Log.Warning(e, "OBR source error while stopping"); }
    }

    _udp?.Dispose();
    _cts.Dispose();
  }

  public void Dispose()
  {
    if (_disposed) return;
    StopAsync(CancellationToken.None).GetAwaiter().GetResult();
  }

  private async Task ReceiveLoopAsync(CancellationToken ct)
  {
    if (_udp == null) return;

    while (!ct.IsCancellationRequested)
    {
      UdpReceiveResult result;
      try
      {
        result = await _udp.ReceiveAsync(ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (ObjectDisposedException)
      {
        break;
      }
      catch (Exception e)
      {
        Log.Warning(e, "OBR source receive error");
        continue;
      }

      var senderIp = result.RemoteEndPoint.Address.ToString();

      if (!string.IsNullOrWhiteSpace(configuration.AllowedIp) && senderIp != configuration.AllowedIp)
        continue;

      string msg;
      try
      {
        msg = Encoding.UTF8.GetString(result.Buffer).Trim();
      }
      catch
      {
        Log.Warning("OBR source received non-UTF8 data from {ip}", senderIp);
        continue;
      }

      if (msg.StartsWith("SI:", StringComparison.Ordinal))
      {
        HandlePunch(msg, senderIp);
      }
      else if (msg.StartsWith("CH|", StringComparison.Ordinal))
      {
        HandleCallHome(msg, senderIp);
      }
      else
      {
        Log.Debug("OBR source unknown message from {ip}: {msg}", senderIp, msg.Length > 60 ? msg[..60] : msg);
      }
    }
  }

  private void HandlePunch(string msg, string senderIp)
  {
    try
    {
      string? cardStr = null, cpStr = null, tmStr = null;
      foreach (var token in msg.Split('|'))
      {
        var sep = token.IndexOf(':');
        if (sep <= 0) continue;
        var key = token[..sep].Trim();
        var val = token[(sep + 1)..].Trim();
        switch (key)
        {
          case "SI": cardStr = val; break;
          case "CP": cpStr = val; break;
          case "TM": tmStr = val; break;
        }
      }

      if (string.IsNullOrEmpty(cardStr) || string.IsNullOrEmpty(cpStr))
      {
        Log.Warning("OBR malformed punch from {ip}: {msg}", senderIp, msg);
        return;
      }

      if (!int.TryParse(cardStr, out var card) || !int.TryParse(cpStr, out var cp))
      {
        Log.Warning("OBR non-numeric card/CP from {ip}: card='{card}' cp='{cp}'", senderIp, cardStr, cpStr);
        return;
      }

      var time = ParseTm(tmStr);

      var punch = filterService.Transform(
        configuration.Filter,
        new Punch(
          ReceivedAt: DateTimeOffset.UtcNow,
          CompetitorId: card.ToString(),
          CompetitorIdType: CompetitorIdType.PunchingCard,
          Control: cp,
          ControlType: PunchControlType.Unknown,
          Time: time,
          SourceId: ResolveSourceId(senderIp)
        )
      );

      if (punch != null)
      {
        dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
      }
      else
      {
        Log.Debug("OBR punch filtered out card={card} cp={cp} tm={tm} from {ip}", card, cp, tmStr ?? "(now)", senderIp);
      }
    }
    catch (Exception e)
    {
      Log.Error(e, "OBR error parsing punch '{msg}'", msg);
    }
  }

  private static DateTime ParseTm(string? tm)
  {
    var now = DateTime.Now;
    if (string.IsNullOrEmpty(tm) || tm.Length != 6)
      return now;

    if (!int.TryParse(tm[0..2], out var h) ||
        !int.TryParse(tm[2..4], out var m) ||
        !int.TryParse(tm[4..6], out var s))
      return now;

    if (h < 0 || h >= 24 || m < 0 || m >= 60 || s < 0 || s >= 60)
      return now;

    return new DateTime(now.Year, now.Month, now.Day, h, m, s);
  }

  private void HandleCallHome(string msg, string senderIp)
  {
    try
    {
      var parts = msg.Split('|');
      if (parts.Length < 2) return;

      var name = parts[1].Trim();
      string? battery = null;

      for (int i = 2; i < parts.Length; i++)
      {
        var token = parts[i];
        var sep = token.IndexOf(':');
        if (sep <= 0) continue;
        var key = token[..sep].Trim();
        var val = token[(sep + 1)..].Trim();
        if (key == "B")
          battery = val.Replace("%", "");
      }

      Log.Debug("OBR call-home node={name} battery={battery}% ip={ip}", name, battery ?? "?", senderIp);
    }
    catch (Exception e)
    {
      Log.Warning(e, "OBR error parsing call-home '{msg}'", msg);
    }
  }
}

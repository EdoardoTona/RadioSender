using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public class MicroplusServer(
  IDispatchSink dispatcherService,
  MicroplusServerConfiguration configuration)
  : UdpServer(IPAddress.Any, configuration.Port ?? throw new ArgumentNullException(nameof(configuration))), ISource, IRadioSenderHost
{
  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (!Start()) throw new System.IO.IOException("Unable to bind the Microplus UDP listener.");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    Stop();
    return Task.CompletedTask;
  }

  protected override void OnStarted()
  {
    // Start receive datagrams
    dispatcherService.Logger.Information($"Microplus started on port {Port}");
    ReceiveAsync();
  }

  protected override void OnError(SocketError error)
  {
    dispatcherService.Logger.Warning("Microplus server socket error {error}", error);
  }

  protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
  {
    string text = string.Empty;
    string textHex = string.Empty;
    try
    {
      var b = buffer.AsSpan((int)offset, (int)size);
      text = Encoding.UTF8.GetString(b);
      textHex = Convert.ToHexString(b);

      var start = text[0];

      if (start != '$')
      {
        dispatcherService.Logger.Warning($"Invalid start character. Received: {text} - {textHex}");
        return;
      }

      var cmd = text[1];

      if (!int.TryParse(text.AsSpan(3, 3), out var order))
        order = 0;

      if (!int.TryParse(text.AsSpan(7, 3), out var bib))
      {
        dispatcherService.Logger.Warning($"Bib missing. Ignored. Received: {text} - {textHex}");
        return;
      }

      if (!int.TryParse(text.AsSpan(11, 3), out var control))
        control = 0;

      var hh = int.Parse(text.AsSpan(15, 2));
      var mm = int.Parse(text.AsSpan(17, 2));
      var ss = int.Parse(text.AsSpan(19, 2));
      var fff = int.Parse(text.AsSpan(21, 3));

      var netTime = cmd == 'T';

      DateTime dt;

      if (netTime)
      {
        dt = new DateTime() + new TimeSpan(0, hh, mm, ss, fff);
      }
      else
      {
        dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, hh, mm, ss, fff);
      }

      var punch = new Punch(
        ReceivedAt: DateTimeOffset.UtcNow,
        CompetitorId: bib.ToString(),
        CompetitorIdType: CompetitorIdType.BibNumber,
        Control: control,
        ControlType: PunchControlType.Unknown,
        Time: dt,
        SourceId: "Microplus",
        Cancellation: false,
        NetTime: netTime);

      if (configuration.IgnoreCommands != null)
      {
        if (configuration.IgnoreCommands.Any(c => c == cmd.ToString()))
        {
          dispatcherService.Logger.Information("Cmd {cmd} ignored. Received: {@punch}", cmd, punch);
          return;
        }
      }

      dispatcherService.PushDispatch(new PunchDispatch([punch]));
    }
    catch (Exception e)
    {
      dispatcherService.Logger.Error(e, $"Error Microplus OnReceived. Received: {text} - {textHex}");
    }
    finally
    {
      if (IsStarted) ReceiveAsync();
    }
  }

}

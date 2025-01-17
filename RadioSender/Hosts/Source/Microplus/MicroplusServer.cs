using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public class MicroplusServer : UdpServer, ISource, IHostedService
{
  private readonly IFilter _filter = Filter.Invariant;
  private readonly MicroplusServerConfiguration _configuration;
  private readonly DispatcherService _dispatcherService;

  public MicroplusServer(
    IEnumerable<IFilter> filters,
    DispatcherService dispatcherService,
    MicroplusServerConfiguration configuration) : base(IPAddress.Any, configuration.Port ?? throw new ArgumentNullException(nameof(configuration)))
  {
    _dispatcherService = dispatcherService;
    _configuration = configuration;
    _filter = filters.GetFilter(_configuration.Filter);
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    Start();
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
    Log.Information($"Microplus started on port {Port}");
    ReceiveAsync();
  }

  protected override void OnError(SocketError error)
  {
    Log.Warning("Microplus server socket error {error}", error);
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
        Log.Warning($"Invalid start character. Received: {text} - {textHex}");
        return;
      }

      var cmd = text[1];

      if (!int.TryParse(text.AsSpan(3, 3), out var order))
        order = 0;

      if (!int.TryParse(text.AsSpan(7, 3), out var bib))
      {
        Log.Warning($"Bib missing. Ignored. Received: {text} - {textHex}");
        return;
      }

      if (!int.TryParse(text.AsSpan(11, 3), out var control))
        control = 999;

      var hh = int.Parse(text.AsSpan(15, 2));
      var mm = int.Parse(text.AsSpan(17, 2));
      var ss = int.Parse(text.AsSpan(19, 2));
      var fff = int.Parse(text.AsSpan(21, 3));

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, hh, mm, ss, fff);

      var punch = _filter.Transform(
                    new Punch(
                    Card: bib.ToString(),
                    Control: control,
                    ControlType: PunchControlType.Unknown,
                    Time: dt,
                    SourceId: "Microplus",
                    Cancellation: false
                    )
                 );

      if (cmd != 'S')
      {
        // filter only S event
        Log.Information("Cmd {cmd} ignored. Received: {@punch}", cmd, punch);
        return;
      }

      if (punch != null)
        _dispatcherService.PushDispatch(new PunchDispatch([punch]));
    }
    catch (Exception e)
    {
      Log.Error(e, $"Error Microplus OnReceived. Received: {text} - {textHex}");
    }
    finally
    {
      ReceiveAsync();
    }
  }

}

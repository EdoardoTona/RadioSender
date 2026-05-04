using ESC_POS_USB_NET.Printer;
using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.PosPrinter;

public class PrinterTarget : ITarget, IDisposable
{
  private readonly FilterService _filterService;
  private readonly PrinterTargetConfiguration _configuration;
  private readonly IHostApplicationLifetime _hostApplicationLifetime;

  public PrinterTarget(
      FilterService filterService,
      PrinterTargetConfiguration configuration,
      IHostApplicationLifetime hostApplicationLifetime)
  {
    _filterService = filterService;
    _configuration = configuration;
    _hostApplicationLifetime = hostApplicationLifetime;
    InitializePrinter();

    _hostApplicationLifetime.ApplicationStopping.Register(() =>
    {
      var printer = new Printer(_configuration.PrinterName);
      printer.NewLine();
      printer.Separator();
      printer.NewLine();
      printer.NewLine();
      printer.FullPaperCut();
      printer.PrintDocument();
    });
  }

  private void InitializePrinter()
  {
    try
    {
      if (string.IsNullOrEmpty(_configuration.PrinterName))
      {
        Log.Warning("Printer name not specified");
        return;
      }

      EncodingProvider ppp = CodePagesEncodingProvider.Instance;
      Encoding.RegisterProvider(ppp);

      var printer = new Printer(_configuration.PrinterName);
      printer.Separator();
      printer.DoubleWidth3();
      printer.AlignCenter();
      printer.Append("Radiosender");
      printer.NormalWidth();
      printer.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss,fff"));
      printer.Separator();

      printer.PrintDocument();

      Log.Information($"ESC/POS USB Printer '{_configuration.PrinterName}' initialized successfully");
    }
    catch (Exception ex)
    {
      Log.Error(ex, $"Failed to initialize ESC/POS USB printer '{_configuration.PrinterName}'");
    }
  }

  public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
  {
    foreach (var dispatch in dispatches)
      SendDispatch(dispatch, ct);

    return Task.CompletedTask;
  }

  public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
  {
    if (dispatch.Punches == null)
      return Task.CompletedTask;

    var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

    foreach (var punch in punches)
    {
      PrintPunch(punch);
    }

    return Task.CompletedTask;
  }

  DateTimeOffset previous;

  private void PrintPunch(Punch punch)
  {
    try
    {
      var format = _configuration.Format ?? "{CompetitorId} {Type}{Control} {Time:HH:mm:ss,fff} {Source} {Status} {Cancellation}";
      var line = FormatStringHelper.GetString(punch, format);

      var _printer = new Printer(_configuration.PrinterName);

      if (punch.Time - previous > TimeSpan.FromMinutes(1) || punch.Time.Minute != previous.Minute)
      {
        _printer.NewLine();
      }

      _printer.Append(line);
      _printer.PrintDocument();

      previous = punch.Time;


    }
    catch (Exception ex)
    {
      Log.Error(ex, $"Failed to print punch: {punch}");
    }
  }

  public void Dispose()
  {

  }
}
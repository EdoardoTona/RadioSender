using ESC_POS_USB_NET.Printer;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.PosPrinter;

[SupportedOSPlatform("windows")]
public class PrinterTarget : ITarget, IDisposable
{
  private readonly FilterService _filterService;
  private readonly PrinterTargetConfiguration _configuration;

  public PrinterTarget(
      FilterService filterService,
      PrinterTargetConfiguration configuration)
  {
    _filterService = filterService;
    _configuration = configuration;
    InitializePrinter();
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

      // Crea il printer usando ESC-POS-USB-NET
      System.Text.EncodingProvider ppp = System.Text.CodePagesEncodingProvider.Instance;
      Encoding.RegisterProvider(ppp);

      var printer = new Printer(_configuration.PrinterName);
      printer.Append("Printer Initialized");
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

  private void PrintPunch(Punch punch)
  {
    try
    {
      var format = _configuration.Format ?? "{Card} {Control} {Time:HH:mm:ss}";
      var line = FormatStringHelper.GetString(punch, format);

      var _printer = new Printer(_configuration.PrinterName);

      // Stampa la riga

      _printer.Append(line);
      _printer.PrintDocument();


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
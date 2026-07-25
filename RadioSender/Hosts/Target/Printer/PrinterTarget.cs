using ESC_POS_USB_NET.Printer;
using ESC_POS_USB_NET.Enums;
using Microsoft.Extensions.Hosting;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.PosPrinter;

public class PrinterTarget : ITarget, IDisposable
{
  private const int PrinterLineWidth = 48;

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
      printer.Append($"Radiosender");
      printer.NormalWidth();
      printer.Append(FitLine($"v{GetAssemblyVersion()} - {Environment.MachineName}"));
      printer.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss,fff"));
      printer.Separator();
      printer.AlignLeft();

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

    var format = _configuration.Format ?? "{CompetitorId} {Type}-{Control} {Time:HH:mm:ss,ffff} {Source} {Status} {Cancellation}";
    var sendsCancellation = FormatStringHelper.UsesPlaceholder(format, "Cancellation");
    var sendsStatus = FormatStringHelper.UsesPlaceholder(format, "Status");

    foreach (var punch in punches)
    {
      // The receiver has no way to tell a cancellation/status change apart from a normal
      // punch unless the format carries it explicitly, so skip what it can't represent.
      if (punch.Cancellation && !sendsCancellation)
        continue;
      if (punch.CompetitorStatus != CompetitorStatus.Unknown && !sendsStatus)
        continue;

      PrintPunch(punch);
    }

    return Task.CompletedTask;
  }

  DateTimeOffset previous;

  private void PrintPunch(Punch punch)
  {
    try
    {
      var format = _configuration.Format ?? "{CompetitorId} {Type}-{Control} {Time:HH:mm:ss,ffff} {Source} {Status} {Cancellation}";
      var line = FormatColumns(punch, format);

      var _printer = new Printer(_configuration.PrinterName);

      if (punch.Time - previous > TimeSpan.FromMinutes(1) || punch.Time.Minute != previous.Minute)
      {
        _printer.NewLine();
      }

      PrintPunchLine(_printer, line, punch.Cancellation);
      _printer.PrintDocument();

      previous = punch.Time;


    }
    catch (Exception ex)
    {
      Log.Error(ex, $"Failed to print punch: {punch}");
    }
  }

  private static void PrintPunchLine(Printer printer, string line, bool isCancellation)
  {
    if (!isCancellation)
    {
      printer.Append(line);
      return;
    }

    printer.UnderlineMode(PrinterModeState.On);
    printer.BoldMode(PrinterModeState.On);
    printer.Append(line);
    printer.BoldMode(PrinterModeState.Off);
    printer.UnderlineMode(PrinterModeState.Off);
  }

  private string FormatColumns(Punch punch, string format)
  {
    var columns = format
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(columnFormat => new
        {
          Format = columnFormat,
          Value = FormatStringHelper.GetString(punch, columnFormat)
        })
        .ToArray();

    return string.Concat(columns.Select((column, index) =>
    {
      if (index == columns.Length - 1)
        return column.Value.TrimEnd();

      var width = GetColumnWidth(column.Format, index);
      var value = FitColumn(column.Value, width);
      var paddedValue = IsCompetitorIdColumn(column.Format)
          ? value.PadLeft(width)
          : value.PadRight(width);

      return $"{paddedValue} ";
    })).TrimEnd();
  }

  private int GetColumnWidth(string columnFormat, int index)
  {
    if (_configuration.ColumnWidths != null &&
        index < _configuration.ColumnWidths.Length &&
        _configuration.ColumnWidths[index] > 0)
      return _configuration.ColumnWidths[index];

    if (ContainsAny(columnFormat, "CompetitorId", "Card", "Bib"))
      return 7;

    if (ContainsAny(columnFormat, "Type", "Control"))
      return 7;

    if (ContainsAny(columnFormat, "Time"))
      return 13;

    if (ContainsAny(columnFormat, "Source"))
      return 10;

    if (ContainsAny(columnFormat, "Status"))
      return 3;

    if (ContainsAny(columnFormat, "Cancellation"))
      return 3;

    return 8;
  }

  private static bool ContainsAny(string value, params string[] needles)
  {
    return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
  }

  private static bool IsCompetitorIdColumn(string columnFormat)
  {
    return ContainsAny(columnFormat, "CompetitorId", "Card", "Bib");
  }

  private static string FitColumn(string value, int width)
  {
    value = value.Trim();

    if (value.Length <= width)
      return value;

    return value[..width];
  }

  private static string FitLine(string value)
  {
    value = value.Trim();

    if (value.Length <= PrinterLineWidth)
      return value;

    return value[..PrinterLineWidth];
  }

  private static string GetAssemblyVersion()
  {
    var version = Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version;

    if (version == null)
      return "unknown";

    return version.Build >= 0
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : $"{version.Major}.{version.Minor}";
  }

  public void Dispose()
  {

  }
}

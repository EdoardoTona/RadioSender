using ESC_POS_USB_NET.Printer;
using ESC_POS_USB_NET.Enums;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Target.PosPrinter;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public sealed class PrinterFlowModule(PrinterTargetConfiguration settings) : FlowModule
{
  public const string DefaultFormat = "{CompetitorId} {Type}-{Control} {Time:HH:mm:ss,ffff} {Source} {Status} {Cancellation}";
  private DateTimeOffset? _previous;
  private bool _started;
  public override Task StartAsync(CancellationToken ct)
  {
    if (!OperatingSystem.IsWindows()) throw new FlowException("ESC/POS USB printing is available on Windows only.");
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var printer = new Printer(settings.PrinterName);
    printer.Separator();
    printer.AlignCenter();
    printer.Append("RadioSender");
    printer.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss,fff"));
    printer.Separator();
    printer.AlignLeft();
    printer.PrintDocument();
    _started = true;
    return base.StartAsync(ct);
  }
  public override ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var format = settings.Format ?? DefaultFormat;
    if (FormattedOutput.Encode(punch, format, false, out var reason) == null)
      return ValueTask.FromResult(new DeliveryResult("Suppressed", reason));
    var printer = new Printer(settings.PrinterName);
    if (punch.Cancellation)
    { printer.UnderlineMode(PrinterModeState.On); printer.BoldMode(PrinterModeState.On); }
    if (_previous is { } previous && (punch.Time - previous > TimeSpan.FromMinutes(1) || punch.Time.Minute != previous.Minute))
      printer.NewLine();
    printer.Append(PrinterTarget.FormatPunch(punch, format, settings.ColumnWidths));
    if (punch.Cancellation)
    { printer.UnderlineMode(PrinterModeState.Off); printer.BoldMode(PrinterModeState.Off); }
    printer.PrintDocument();
    _previous = punch.Time;
    return ValueTask.FromResult(new DeliveryResult("Submitted", "Submitted to the Windows printer spooler."));
  }
  public override Task StopAsync(CancellationToken ct)
  {
    if (_started)
    {
      var printer = new Printer(settings.PrinterName);
      printer.NewLine();
      printer.Separator();
      printer.FullPaperCut();
      printer.PrintDocument();
      _started = false;
    }
    return base.StopAsync(ct);
  }
}

using RadioSender.Hosts.Common;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public sealed record FileSettings
{
  [Required, MaxLength(4096), Display(Name = "File path", Description = "Relative paths use the configuration document's folder.")]
  public string Path { get; init; } = "punches.csv";
  [Required, MaxLength(8192), Display(Name = "Output format", Description = "Use {CompetitorId}, {Control}, {Time:O}, {Status}, {Cancellation} and {CRLF}.")]
  public string Format { get; init; } = "{CompetitorId};{Control};{Time:O};{Status};{Cancellation}{CRLF}";
}

public sealed class FileFlowModule(FileSettings settings, ModuleContext context) : FlowModule
{
  private FileStream? _stream;
  public override Task StartAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var path = Path.GetFullPath(settings.Path, context.Directory);
    _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
    SetState("Running", path);
    return Task.CompletedTask;
  }
  public override async ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct)
  {
    var bytes = FormattedOutput.Encode(punch, settings.Format, false, out var reason);
    if (bytes == null) return new("Suppressed", reason);
    if (_stream == null) throw new IOException("The output file is closed.");
    await _stream.WriteAsync(bytes, ct);
    await _stream.FlushAsync(ct);
    return new("Written", "Appended to file.");
  }
  public override async Task StopAsync(CancellationToken ct)
  {
    if (_stream != null) { await _stream.DisposeAsync(); _stream = null; }
    SetState("Stopped");
  }
  public override ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
}

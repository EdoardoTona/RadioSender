using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Target.PosPrinter;

public record PrinterTargetConfiguration
{
  [Required]
  public string? PrinterName { get; set; }
  public string? Format { get; set; }
  public int[]? ColumnWidths { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.SportidentSerial;

public record Port
{
  [Required]
  public string? PortName { get; init; }
  [Range(1, 4000000)]
  public int Baudrate { get; init; } = 38400;
}

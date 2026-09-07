using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.TmFRadio;

public record Gateway
{
  [Required]
  public string? PortName { get; init; }
  [Range(1, 4000000)]
  public int Baudrate { get; init; } = 19200;
  [Range(2, 3600)]
  public int StatusCheck { get; init; } = 60; // seconds
}

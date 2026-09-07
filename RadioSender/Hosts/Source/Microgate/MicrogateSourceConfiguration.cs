using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.Microplus;

public record MicrogateSourceConfiguration
{
  public string? Address { get; init; }
  [Range(1, 65535)]
  public int? Port { get; init; }
  public string? PortName { get; init; }
  [Range(1, 4000000)]
  public int Baudrate { get; init; } = 115200;
  public bool DtrEnable { get; init; }
  public bool RtsEnable { get; init; }
}

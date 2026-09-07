using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.OBR;

public record ObrSourceConfiguration
{
  [Required, Range(1, 65535)]
  public int? Port { get; init; }
  public string? AllowedIp { get; init; }
  public string? SourceId { get; init; }
}

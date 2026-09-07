using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.SIRAP;

public record SirapServerConfiguration
{
  [Required, Range(1, 65535)]
  public int? Port { get; init; }
}

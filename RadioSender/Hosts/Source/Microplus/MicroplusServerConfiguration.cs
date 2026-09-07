using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.Microplus;

public record MicroplusServerConfiguration
{
  [Required, Range(1, 65535)]
  public int? Port { get; init; }
  public IEnumerable<string>? IgnoreCommands { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Target.SIRAP;

public record SirapClientConfiguration
{
  [Required]
  public string? Address { get; init; }
  [Required, Range(1, 65535)]
  public int? Port { get; init; }
  [Range(1, 2)]
  public int Version { get; init; } = 2;
  public TimeSpan ZeroTime { get; init; } = TimeSpan.Zero;
}

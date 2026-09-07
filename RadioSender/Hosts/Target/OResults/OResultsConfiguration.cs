using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Target.OResults;

public record OResultsConfiguration
{
  [Required, Url]
  public string Host { get; init; } = "https://api.oresults.eu/";
  [Required]
  public string Path { get; init; } = "/punches/external";
  [Required]
  public string? ApiKey { get; init; }
  public bool UseUtc { get; init; } = true;
  public bool IgnoreCompetitorIdType { get; init; } = false;
}

using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.ROC;

public record Event
{
  [Required, Range(1, int.MaxValue)]
  public int? EventId { get; init; }
  [Range(100, 3600000)]
  public int RefreshMs { get; init; } = 2000;
  [Required, Url]
  public string Host { get; init; } = "https://roc.olresultat.se/";
  [Required]
  public string Path { get; init; } = "/getpunches.asp?unitId={EventId}&lastId={LastId}";
}

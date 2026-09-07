using System.ComponentModel.DataAnnotations;

namespace RadioSender.Hosts.Source.SportidentCenter;

public record Event
{
  [Required, Range(1, int.MaxValue)]
  public int? EventId { get; init; }
  [Required]
  public string? ApiKey { get; init; }
  [Range(100, 3600000)]
  public int RefreshMs { get; init; } = 1000;
}

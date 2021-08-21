namespace RadioSender.Hosts.Common.Filters
{
  public record FilterableConfiguration : Configuration
  {
    public string Filter { get; init; }
  }
}

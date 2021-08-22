namespace RadioSender.Hosts.Source.TmFRadio
{
  public record RxMsg
  {
    public RxHeader Header { get; init; } = null!;
  }
}

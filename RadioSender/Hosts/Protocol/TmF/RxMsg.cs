namespace RadioSender.Hosts.Protocol.TmF
{
  public record RxMsg
  {
    public RxHeader Header { get; init; } = null!;
  }
}

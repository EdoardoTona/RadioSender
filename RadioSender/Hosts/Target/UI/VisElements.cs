using RadioSender.Hosts.Common;
using System;

namespace RadioSender.Hosts.Target.UI
{
  public record VisEdge(
    string From,
    string To,
    int? Value,
    int? Length,
    string? Label,
    string? Title,
    DateTimeOffset LastSeen)
  {
    public string Id { get => From + "=>" + To; }
    public bool Hidden { get => DateTimeOffset.UtcNow - LastSeen > TimeSpan.FromSeconds(20); }

    public static VisEdge FromHop(Hop hop)
    {
      return new VisEdge(hop.From, hop.To, 7, (int?)hop.LatencyMs / 10, null, hop.LatencyMs + "ms", DateTimeOffset.UtcNow);
    }
  }

  public record VisNode(
    string Id,
    string Label,
    int? Value,
    string? Title,
    int? Size,
    string? Group)
  {

    public static VisNode FromNode(NodeNew node)
    {
      return new VisNode(node.Id, node.Name ?? node.Id, node.SignalStength * 20 + 10, node.LatencyMs + "ms", 10, null);
    }
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hubs.Devices
{
  public record Edge(string From, string To, int? Value, int? Length, string Label, string Tooltip, DateTimeOffset LastSeen)
  {
    public string Id { get => From + "=>" + To; }
    public bool Hidden { get => DateTimeOffset.UtcNow - LastSeen > TimeSpan.FromSeconds(20); }
  }

  public record Node(string Id, string Label, int? Value, string Title, DateTimeOffset LastSeen);
}

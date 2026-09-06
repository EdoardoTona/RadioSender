using RadioSender.Flow.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioSender.Flow;

public sealed class FlowValidator(ModuleRegistry registry)
{
  public IReadOnlyList<FlowIssue> Validate(FlowDocument document)
  {
    FlowJson.CheckStructure(document);
    var issues = new List<FlowIssue>();
    var nodes = document.Nodes.ToDictionary(n => n.Id);
    foreach (var node in document.Nodes)
    {
      var definition = registry.Find(node.Type);
      if (definition == null) issues.Add(new(node.Id, "type", "This module is not available."));
      else if (node.Enabled) issues.AddRange(definition.Validate(node));
    }
    foreach (var edge in document.Edges)
    {
      if (!nodes.TryGetValue(edge.From.Node, out var source) || !nodes.TryGetValue(edge.To.Node, out var target))
      { issues.Add(new(edge.Id, "connection", "Both endpoints must exist.")); continue; }
      if (registry.Find(source.Type)?.Descriptor.Outputs.Contains(edge.From.Port) != true ||
          registry.Find(target.Type)?.Descriptor.Inputs.Contains(edge.To.Port) != true)
        issues.Add(new(edge.Id, "connection", "Connect an output port to a compatible input port."));
      if (edge.Filter is { } f)
      {
        if (f.MapControls == null || f.MapCompetitorIds == null || f.IncludeOnlyControls == null || f.IncludeOnlyCompetitorIds == null ||
            f.TypeFromCode == null || f.TypeFromCode.Any(kv => !Enum.IsDefined(kv.Key) || kv.Value == null) ||
            f.MapControls.Keys.Any(k => !int.TryParse(k, out _)) || f.MapCompetitorIds.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) ||
            !double.IsFinite(f.IgnoreOlderThanSeconds) || f.IgnoreOlderThanSeconds < 0 || f.IgnoreOlderThanSeconds > 315360000 ||
            f.OverrideCompetitorIdType is { } idType && !Enum.IsDefined(idType))
          issues.Add(new(edge.Id, "filter", "Filter lists, mappings, identifier type or maximum age are invalid."));
      }
    }

    // Validate disabled edges too: enabling a connection must not reveal a hidden cycle.
    var indegree = nodes.Keys.ToDictionary(id => id, _ => 0);
    var outgoing = nodes.Keys.ToDictionary(id => id, _ => new List<string>());
    foreach (var e in document.Edges.Where(e => nodes.ContainsKey(e.From.Node) && nodes.ContainsKey(e.To.Node)))
    { indegree[e.To.Node]++; outgoing[e.From.Node].Add(e.To.Node); }
    var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var paths = nodes.Keys.ToDictionary(id => id, _ => 1L);
    var visited = 0;
    while (queue.TryDequeue(out var id))
    {
      visited++;
      foreach (var next in outgoing[id])
      {
        paths[next] = Math.Min(4097, paths[next] + paths[id]);
        if (--indegree[next] == 0) queue.Enqueue(next);
      }
    }
    if (visited != nodes.Count) issues.Add(new("", "edges", "Cycles are not supported. Remove a connection to break the loop."));
    if (paths.Values.Any(p => p > 4096)) issues.Add(new("", "edges", "The graph has too many converging paths."));
    var listeners = document.Nodes.Where(n => n.Enabled && n.Type is "source.tcp" or "target.tcp")
      .Where(n => !issues.Any(i => i.ElementId == n.Id)).Select(n => (Node: n, Settings: registry.Find(n.Type)!.Normalize(n.Settings)))
      .Where(x => x.Settings["asServer"]!.GetValue<bool>()).GroupBy(x => x.Settings["port"]!.GetValue<int>());
    foreach (var group in listeners.Where(g => g.Count() > 1))
      foreach (var item in group) issues.Add(new(item.Node.Id, "settings.port", "Another TCP listener uses this port."));
    return issues;
  }
}

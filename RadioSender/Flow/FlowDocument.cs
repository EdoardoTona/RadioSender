using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RadioSender.Flow;

public sealed record FlowDocument
{
  public int SchemaVersion { get; init; } = 1;
  public List<FlowNode> Nodes { get; init; } = [];
  public List<FlowEdge> Edges { get; init; } = [];
  public FlowEditor Editor { get; init; } = new();
}

public sealed record FlowNode
{
  public string Id { get; init; } = "";
  public string Type { get; init; } = "";
  public string Name { get; init; } = "";
  public bool Enabled { get; init; } = true;
  public JsonObject Settings { get; init; } = new();
}

public sealed record FlowPort(string Node, string Port);
public sealed record FlowEdge
{
  public string Id { get; init; } = "";
  public FlowPort From { get; init; } = new("", "out");
  public FlowPort To { get; init; } = new("", "in");
  public bool Enabled { get; init; } = true;
  public EdgeFilter? Filter { get; init; }
}

public sealed record FlowPosition(double X, double Y);
public sealed record FlowViewport(double X, double Y, double Zoom);
public sealed record FlowEditor
{
  public Dictionary<string, FlowPosition> Positions { get; init; } = [];
  public FlowViewport? Viewport { get; init; }
}

public sealed record EdgeFilter
{
  public bool Enabled { get; init; } = true;
  public Dictionary<string, int> MapControls { get; init; } = [];
  public Dictionary<string, string> MapCompetitorIds { get; init; } = [];
  public HashSet<int> IncludeOnlyControls { get; init; } = [];
  public HashSet<string> IncludeOnlyCompetitorIds { get; init; } = [];
  public Dictionary<PunchControlType, HashSet<int>> TypeFromCode { get; init; } = [];
  public CompetitorIdType? OverrideCompetitorIdType { get; init; }
  public double IgnoreOlderThanSeconds { get; init; }

  public Filter Compile() => new()
  {
    Enable = Enabled,
    MapControls = MapControls,
    MapCompetitorIds = MapCompetitorIds,
    IncludeOnlyControls = IncludeOnlyControls,
    IncludeOnlyCompetitorIds = IncludeOnlyCompetitorIds,
    TypeFromCode = TypeFromCode,
    OverrideCompetitorIdType = OverrideCompetitorIdType,
    IgnoreOlderThan = TimeSpan.FromSeconds(IgnoreOlderThanSeconds)
  };
}

public sealed record FlowIssue(string ElementId, string Field, string Message);
public sealed class FlowException(string message, int status = 400) : Exception(message)
{
  public int Status { get; } = status;
}

public static class FlowJson
{
  public const int MaxDocumentBytes = 2 * 1024 * 1024;
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    MaxDepth = 32,
    Converters = { new JsonStringEnumConverter() }
  };

  public static FlowDocument Read(string json)
  {
    if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
      throw new FlowException("The document exceeds the 2 MB limit.");
    var root = JsonNode.Parse(json, documentOptions: new() { MaxDepth = 32 }) as JsonObject;
    if (root?["schemaVersion"]?.GetValue<int>() != 1)
      throw new FlowException("Unsupported document format. Open a RadioSender graph document with schemaVersion 1.");
    var document = root.Deserialize<FlowDocument>(Options) ?? throw new FlowException("The document is empty.");
    CheckStructure(document);
    return document;
  }

  public static FlowDocument Clone(FlowDocument document) => Read(JsonSerializer.Serialize(document, Options));

  public static void CheckStructure(FlowDocument document)
  {
    if (document.SchemaVersion != 1 || document.Nodes == null || document.Edges == null || document.Editor?.Positions == null)
      throw new FlowException("Invalid graph document structure.");
    if (document.Nodes.Count > 128 || document.Edges.Count > 256)
      throw new FlowException("A document supports up to 128 nodes and 256 edges.");
    if (document.Nodes.Any(n => n == null || n.Settings == null || !ValidId(n.Id) || string.IsNullOrWhiteSpace(n.Type) || n.Name == null) ||
        document.Edges.Any(e => e == null || !ValidId(e.Id) || e.From == null || e.To == null))
      throw new FlowException("Nodes and edges require valid IDs, endpoints and settings.");
    if (document.Nodes.Select(n => n.Id).Distinct().Count() != document.Nodes.Count ||
        document.Edges.Select(e => e.Id).Distinct().Count() != document.Edges.Count)
      throw new FlowException("Node and edge IDs must be unique.");
    if (document.Editor.Positions.Any(p => p.Value == null || !double.IsFinite(p.Value.X) || !double.IsFinite(p.Value.Y)))
      throw new FlowException("Editor positions must be finite numbers.");
  }

  private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100 &&
    id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

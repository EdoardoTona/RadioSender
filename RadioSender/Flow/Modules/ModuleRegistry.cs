using RadioSender.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace RadioSender.Flow.Modules;

public sealed class ModuleRegistry
{
  private readonly Dictionary<string, ModuleDefinition> _definitions;
  public ModuleRegistry(IEnumerable<ModuleDefinition> definitions) =>
    _definitions = definitions.ToDictionary(d => d.Descriptor.Type, StringComparer.Ordinal);
  public IReadOnlyList<ModuleDescriptor> Catalog => _definitions.Values.Select(d => d.Descriptor).ToArray();
  public ModuleDefinition? Find(string type) => _definitions.GetValueOrDefault(type);

  public static ModuleRegistry CreateDefault() => new([
    new ModuleDefinition<EmptySettings>("source.manual", "Manual input", "Source", "Enter punches and competitor status changes.",
      (_, c) => new ManualFlowModule(c.NodeId), commands: [new("send", "Send event")], view: "manual-input"),
    new ModuleDefinition<EmptySettings>("processor.passthrough", "Passthrough", "Processor", "Inspect, replay and branch an unchanged stream.",
      (_, _) => new PassthroughModule()),
    new ModuleDefinition<TcpSettings>("source.tcp", "TCP input", "Source", "Read formatted lines as a TCP client or server.",
      (s, c) => new TcpFlowModule(s, c, true), s => ValidateTcp(s, true)),
    new ModuleDefinition<TcpSettings>("target.tcp", "TCP output", "Target", "Send formatted events as a TCP client or server.",
      (s, c) => new TcpFlowModule(s, c, false), s => ValidateTcp(s, false)),
    new ModuleDefinition<FileSettings>("target.file", "File output", "Target", "Append formatted events to a file.",
      (s, c) => new FileFlowModule(s, c), s => ValidateFormat(s.Format))
  ]);

  private static IEnumerable<ValidationResult> ValidateTcp(TcpSettings settings, bool source)
  {
    if (!settings.AsServer && string.IsNullOrWhiteSpace(settings.Address))
      yield return new("An address is required in client mode.", [nameof(settings.Address)]);
    foreach (var result in ValidateFormat(settings.Format)) yield return result;
    if (source && !string.IsNullOrWhiteSpace(settings.Format) &&
        (!new[] { "CompetitorId", "Card", "Bib" }.Any(p => FormatStringHelper.UsesPlaceholder(settings.Format, p)) ||
         FormatStringParser.TryCreate(settings.Format) == null))
      yield return new("An input format must contain {CompetitorId}, {Card} or {Bib}.", [nameof(settings.Format)]);
  }

  private static IEnumerable<ValidationResult> ValidateFormat(string? format)
  {
    if (string.IsNullOrWhiteSpace(format)) yield break;
    string? error = null;
    try
    {
      FormatStringHelper.GetString(new("1", DateTime.Now, 35, "validation", DateTimeOffset.UtcNow), format);
    }
    catch (FormatException) { error = "The output format contains an invalid format specifier."; }
    if (error != null) yield return new(error, ["Format"]);
  }
}

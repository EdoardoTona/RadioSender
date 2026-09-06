using RadioSender.Hosts.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public interface IFlowOutput
{
  ValueTask PublishAsync(Punch punch, CancellationToken cancellationToken);
}

public record DeliveryResult(string Status, string? Detail = null);
public record ModuleState(string Status, string? Detail = null);
public record CommandResult(string Message, IReadOnlyList<Punch>? Output = null);
public record ModuleCommand(string Id, string Label);
public record ModuleField(string Key, string Label, string Kind, bool Required, double? Min, double? Max, string? Help);
public record ModuleDescriptor(string Type, string Name, string Category, string Description,
  string[] Inputs, string[] Outputs, JsonObject Defaults, IReadOnlyList<ModuleField> Fields,
  IReadOnlyList<ModuleCommand> Commands, string? View = null);

public abstract class FlowModule : IRadioSenderHost, IAsyncDisposable
{
  private ModuleState _state = new("Stopped");
  private ILogger _logger = Log.Logger;
  internal void AttachLogging(string nodeId, Guid sessionId) => _logger = Log.ForContext("NodeId", nodeId).ForContext("SessionId", sessionId);
  public ModuleState State => Volatile.Read(ref _state);
  protected void SetState(string status, string? detail = null)
  {
    var next = new ModuleState(status, detail);
    var previous = Interlocked.Exchange(ref _state, next);
    if (previous == next) return;
    if (status is "Error" or "Disconnected" or "Input warning") _logger.Warning("{Status}: {Detail}", status, detail);
    else _logger.Information("{Status}: {Detail}", status, detail);
  }
  public virtual Task StartAsync(CancellationToken ct) { SetState("Running"); return Task.CompletedTask; }
  public virtual Task StopAsync(CancellationToken ct) { SetState("Stopped"); return Task.CompletedTask; }
  public virtual ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct) =>
    ValueTask.FromResult(new DeliveryResult("Accepted"));
  public virtual ValueTask<CommandResult> ExecuteCommandAsync(string command, JsonObject arguments, CancellationToken ct) =>
    throw new FlowException("This module does not support the requested command.");
  protected ILogger Logger => _logger;
  public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record ModuleContext(string NodeId, string Directory, IFlowOutput Output);

public abstract class ModuleDefinition
{
  public abstract ModuleDescriptor Descriptor { get; }
  public abstract JsonObject Normalize(JsonObject settings);
  public abstract IEnumerable<FlowIssue> Validate(FlowNode node);
  public abstract FlowModule Create(JsonObject settings, ModuleContext context);
}

public sealed class ModuleDefinition<TSettings>(string type, string name, string category, string description,
  Func<TSettings, ModuleContext, FlowModule> factory,
  Func<TSettings, IEnumerable<ValidationResult>>? validate = null,
  ModuleCommand[]? commands = null, string? view = null) : ModuleDefinition where TSettings : new()
{
  public override ModuleDescriptor Descriptor { get; } = new(type, name, category, description,
    category == "Source" ? [] : ["in"], category == "Target" ? [] : ["out"],
    JsonSerializer.SerializeToNode(new TSettings(), FlowJson.Options)!.AsObject(), GetFields(), commands ?? [], view);

  private static IReadOnlyList<ModuleField> GetFields() => typeof(TSettings).GetProperties().Select(p =>
  {
    var display = p.GetCustomAttribute<DisplayAttribute>();
    var range = p.GetCustomAttribute<RangeAttribute>();
    return new ModuleField(JsonNamingPolicy.CamelCase.ConvertName(p.Name), display?.Name ?? p.Name,
      p.PropertyType == typeof(bool) ? "boolean" : p.PropertyType == typeof(int) ? "number" : "text",
      p.IsDefined(typeof(RequiredAttribute)), range == null ? null : Convert.ToDouble(range.Minimum),
      range == null ? null : Convert.ToDouble(range.Maximum), display?.Description);
  }).ToList();

  private static TSettings Read(JsonObject settings) => settings.Deserialize<TSettings>(FlowJson.Options) ?? new();
  public override JsonObject Normalize(JsonObject settings) => JsonSerializer.SerializeToNode(Read(settings), FlowJson.Options)!.AsObject();
  public override FlowModule Create(JsonObject settings, ModuleContext context) => factory(Read(settings), context);
  public override IEnumerable<FlowIssue> Validate(FlowNode node)
  {
    var issues = new List<FlowIssue>();
    try
    {
      var known = Descriptor.Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var key in node.Settings.Select(kv => kv.Key).Where(key => !known.Contains(key)))
        issues.Add(new(node.Id, "settings." + key, "Unknown setting: " + key));
      var settings = Read(node.Settings);
      var results = new List<ValidationResult>();
      Validator.TryValidateObject(settings!, new ValidationContext(settings!), results, true);
      if (validate != null) results.AddRange(validate(settings));
      issues.AddRange(results.Select(r => new FlowIssue(node.Id,
        "settings." + JsonNamingPolicy.CamelCase.ConvertName(r.MemberNames.FirstOrDefault() ?? ""), r.ErrorMessage ?? "Invalid setting.")));
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
    {
      issues.Add(new(node.Id, "settings", "Settings contain an invalid value or type: " + e.Message));
    }
    return issues;
  }
}

public sealed record EmptySettings;
public sealed class PassthroughModule : FlowModule;

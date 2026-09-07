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
public record ModuleField(string Key, string Label, string Kind, bool Required, double? Min, double? Max, string? Help, string[]? Choices = null, string? ItemKind = null);
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
  public virtual Punch? Process(Punch punch, bool replay) => punch;
  public virtual object? ViewData() => null;
  public virtual Task StartAsync(CancellationToken ct) { SetState("Running"); return Task.CompletedTask; }
  public virtual Task StopAsync(CancellationToken ct) { SetState("Stopped"); return Task.CompletedTask; }
  public virtual ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct) =>
    ValueTask.FromResult(new DeliveryResult("Accepted"));
  public virtual ValueTask<CommandResult> ExecuteCommandAsync(string command, JsonObject arguments, CancellationToken ct) =>
    throw new FlowException("This module does not support the requested command.");
  protected ILogger Logger => _logger;
  public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record ModuleContext(string NodeId, string Directory, IFlowOutput Output, Func<string, FlowModule?>? Resolve = null);

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
    category is "Source" or "Provider" ? [] : ["in"], category == "Target" ? [] : ["out"],
    ToJson(new TSettings()), GetFields(), commands ?? [], view);

  private static IEnumerable<PropertyInfo> Properties => typeof(TSettings).GetProperties()
    .Where(p => p.Name is not ("Filter" or "Enable") && !p.IsDefined(typeof(ObsoleteAttribute)));
  private static JsonObject ToJson(TSettings settings)
  {
    var json = JsonSerializer.SerializeToNode(settings, FlowJson.Options)!.AsObject();
    var keys = Properties.Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToHashSet();
    foreach (var key in json.Select(x => x.Key).Where(k => !keys.Contains(k)).ToArray()) json.Remove(key);
    return json;
  }
  private static IReadOnlyList<ModuleField> GetFields() => Properties.Select(p =>
  {
    var display = p.GetCustomAttribute<DisplayAttribute>();
    var range = p.GetCustomAttribute<RangeAttribute>();
    var valueType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
    var itemType = valueType.IsArray ? valueType.GetElementType() :
      valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? valueType.GetGenericArguments()[0] : null;
    var choicesType = itemType ?? valueType;
    var kind = itemType != null ? "list" : valueType.IsEnum ? "select" : valueType == typeof(bool) ? "boolean" :
      valueType == typeof(int) ? "number" : p.Name == "ProviderId" ? "provider" :
      p.Name is "Password" or "ApiKey" ? "password" : "text";
    return new ModuleField(JsonNamingPolicy.CamelCase.ConvertName(p.Name), display?.Name ??
      System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z])([A-Z])", "$1 $2"), kind,
      p.IsDefined(typeof(RequiredAttribute)), range == null ? null : Convert.ToDouble(range.Minimum),
      range == null ? null : Convert.ToDouble(range.Maximum), display?.Description,
      choicesType.IsEnum ? Enum.GetNames(choicesType) : null, itemType == typeof(int) ? "number" : "text");
  }).ToList();

  private static TSettings Read(JsonObject settings) => settings.Deserialize<TSettings>(FlowJson.Options) ?? new();
  public override JsonObject Normalize(JsonObject settings) => ToJson(Read(settings));
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
      foreach (var property in Properties.Where(p => p.IsDefined(typeof(UrlAttribute))))
        if (property.GetValue(settings) is string url &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
          results.Add(new("Use an HTTP(S) server address.", [property.Name]));
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

using RadioSender.Hosts.Common;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public sealed class ManualFlowModule(string nodeId) : FlowModule
{
  public override ValueTask<CommandResult> ExecuteCommandAsync(string command, JsonObject arguments, CancellationToken ct)
  {
    if (command != "send") return base.ExecuteCommandAsync(command, arguments, ct);
    var punch = arguments["punch"]?.Deserialize<Punch>(FlowJson.Options);
    if (punch == null || string.IsNullOrWhiteSpace(punch.CompetitorId) || punch.CompetitorId.Length > 256 || punch.Time == default ||
        !Enum.IsDefined(punch.CompetitorIdType) || !Enum.IsDefined(punch.ControlType) || !Enum.IsDefined(punch.CompetitorStatus) || punch.Control < 0)
      throw new FlowException("Enter a competitor ID, valid time, control and event types.");
    punch = punch with { SourceId = nodeId, ReceivedAt = DateTimeOffset.UtcNow, Competitor = null };
    return ValueTask.FromResult(new CommandResult("Event accepted.", [punch]));
  }
}

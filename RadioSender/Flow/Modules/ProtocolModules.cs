using RadioSender.Hosts.Source.Microplus;
using RadioSender.Hosts.Source.Mqtt;
using RadioSender.Hosts.Source.OBR;
using RadioSender.Hosts.Source.SIRAP;
using RadioSender.Hosts.Source.SportidentSerial;
using RadioSender.Hosts.Source.SportidentCenter;
using RadioSender.Hosts.Source.TmFRadio;
using RadioSender.Hosts.Target.SIRAP;
using RadioSender.Hosts.Target.OResults;
using RadioSender.Hosts.Target.PosPrinter;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace RadioSender.Flow.Modules;

public static class ProtocolModules
{
  public static IEnumerable<ModuleDefinition> Definitions => [
    new ModuleDefinition<DeduplicateSettings>("processor.deduplicate", "Deduplicate", "Processor",
      "Suppress repeated events within bounded history. Cancellation and restoration pass through; replay bypasses history.",
      (s, _) => new DeduplicateModule(s)),
    new ModuleDefinition<EnrichmentSettings>("processor.enrichment", "Enrichment", "Processor",
      "Attach competitor data from a shared provider.", (s, c) => new EnrichmentModule(s, c)),
    new ModuleDefinition<OribosProviderSettings>("provider.oribos", "Oribos data", "Provider",
      "Share a competitor lookup and optionally emit status changes on the output port.",
      (s, c) => new OribosProviderModule(s, c), commands: [new("refresh", "Reload data")]),
    new ModuleDefinition<MicrogateSourceConfiguration>("source.microgate", "Microgate", "Source",
      "Receive REI2 events over TCP or serial. Set a serial port name to use serial mode.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => string.IsNullOrWhiteSpace(s.PortName) ?
        new MicrogateTcpSource(f, d, s) : new MicrogateSerialSource(f, d, s)), ValidateMicrogate),
    new ModuleDefinition<MicroplusServerConfiguration>("source.microplus", "Microplus", "Source", "Receive Microplus UDP events.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new MicroplusServer(f, d, s))),
    new ModuleDefinition<ObrSourceConfiguration>("source.obr", "OBR", "Source", "Receive OBR UDP events.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new ObrSource(f, d, s)), s =>
        string.IsNullOrWhiteSpace(s.AllowedIp) || System.Net.IPAddress.TryParse(s.AllowedIp, out _) ? [] : [new("Enter a valid allowed IP address.", ["AllowedIp"])]),
    new ModuleDefinition<SirapServerConfiguration>("source.sirap", "SIRAP input", "Source", "Receive SIRAP over TCP.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new SirapServer(f, d, s))),
    new ModuleDefinition<Port>("source.sportident-serial", "SPORTident serial", "Source", "Read a SPORTident station over serial.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new SportidentSerialPort(f, d, s))),
    new ModuleDefinition<Event>("source.sportident-center", "SPORTident Center", "Source", "Poll a SPORTident Center event.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new SportidentCenterEvent(f, new SourceHttpClientFactory("https://center.sportident.com/"), d, s))),
    new ModuleDefinition<Hosts.Source.ROC.Event>("source.roc", "ROC", "Source", "Poll events from ROC.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new Hosts.Source.ROC.ROCEvent(new SourceHttpClientFactory(s.Host), d, f, s, s.EventId!.Value))),
    new ModuleDefinition<MqttSourceConfiguration>("source.mqtt", "MQTT", "Source", "Subscribe to SPORTident or TmF payloads.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new MqttSource(f, d, s)), ValidateMqtt),
    new ModuleDefinition<Gateway>("source.tmf", "TmF Radio", "Source", "Receive radio events and inspect this gateway's radio network.",
      (s, c) => new ProtocolSourceModule(c, (f, d) => new TmFRadioGateway(f, d, s), radio: true),
      commands: [new("ping", "Ping radios")], view: "radio-network"),
    new ModuleDefinition<HttpSettings>("target.http", "HTTP output", "Target", "Send one HTTP request per event.",
      (s, _) => HttpDeliveryModule.Http(s), s => Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && Enum.IsDefined(s.Method) ? [] : [new("Enter an HTTP(S) URL and a supported method.", ["Url"])]),
    new ModuleDefinition<OribosTargetSettings>("target.oribos", "Oribos output", "Target", "Send punches and supported status changes to Oribos.",
      (s, _) => HttpDeliveryModule.Oribos(s)),
    new ModuleDefinition<OResultsConfiguration>("target.oresults", "OResults", "Target", "Send punching-card events to OResults.",
      (s, _) => HttpDeliveryModule.OResults(s)),
    new ModuleDefinition<SirapClientConfiguration>("target.sirap", "SIRAP output", "Target", "Send SIRAP v1 or v2 over TCP.",
      (s, c) => new TcpFlowModule(new() { Address = s.Address!, Port = s.Port!.Value }, c, false,
        p => p.CompetitorStatus != Hosts.Common.CompetitorStatus.Unknown || p.NetTime ? null : Hosts.Target.SIRAP.SirapClient.Encode(p, s.Version, s.ZeroTime)),
      s => s.ZeroTime < TimeSpan.Zero || s.ZeroTime >= TimeSpan.FromDays(1) ? [new("Zero time must be within one day.", ["ZeroTime"])] : []),
    new ModuleDefinition<PrinterTargetConfiguration>("target.printer", "ESC/POS printer", "Target", "Print formatted events through the Windows USB printer spooler.",
      (s, _) => new PrinterFlowModule(s), s => !OperatingSystem.IsWindows() ? [new("ESC/POS printing requires Windows.")] :
        s.ColumnWidths?.Any(w => w is < 1 or > 200) == true ? [new("Column widths must be between 1 and 200.", ["ColumnWidths"])] : [])
  ];

  private static IEnumerable<ValidationResult> ValidateMicrogate(MicrogateSourceConfiguration s)
  {
    if (string.IsNullOrWhiteSpace(s.PortName) && (string.IsNullOrWhiteSpace(s.Address) || s.Port == null))
      yield return new("Set an address and TCP port, or a serial port name.", ["Address"]);
  }
  private static IEnumerable<ValidationResult> ValidateMqtt(MqttSourceConfiguration s)
  {
    if (s.Topics == null || s.Topics.Length == 0 || s.Topics.Any(string.IsNullOrWhiteSpace))
      yield return new("Add at least one nonempty MQTT topic.", ["Topics"]);
    if (s.Protocols == null || s.Protocols.Length == 0 || s.Protocols.Any(p => !Enum.IsDefined(p)))
      yield return new("Select at least one supported payload protocol.", ["Protocols"]);
    if (!Enum.IsDefined(s.ProtocolVersion)) yield return new("Select a supported MQTT version.", ["ProtocolVersion"]);
  }
}

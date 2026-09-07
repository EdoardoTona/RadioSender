using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet.Formatter;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.Mqtt;

public enum MqttPayloadProtocol
{
  Sportident,
  TmF
}

public record MqttSourceConfiguration : FilterableConfiguration
{
  [Required]
  public string? Host { get; init; }
  [Range(1, 65535)]
  public int? Port { get; init; }
  public bool UseWebSocket { get; init; }
  public bool UseTls { get; init; }
  public string? WebSocketPath { get; init; } = "/mqtt";
  public string[] Topics { get; init; } = [];
  public MqttPayloadProtocol[] Protocols { get; init; } = [MqttPayloadProtocol.Sportident];
  public string? SourceId { get; init; }
  public string? ClientId { get; init; }
  public string? Username { get; init; }
  public string? Password { get; init; }
  public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V500;
  [Range(1, 120)]
  public int TimeoutSeconds { get; init; } = 10;
  [Range(1, 3600)]
  public int KeepAliveSeconds { get; init; } = 15;
  [Range(1, 300)]
  public int ReconnectDelaySeconds { get; init; } = 1;
  public bool CleanSession { get; init; } = true;
  [Range(0, 2)]
  public int QualityOfService { get; init; } = 1;
}

public static class ConfigureMqttSource
{
  public static IHostBuilder FromMqtt(this IHostBuilder builder)
  {
    builder.ConfigureServices((context, services) =>
    {
      if (!context.Configuration.GetValue("Source:MQTT:Enable", false))
        return;

      var sources = context.Configuration.GetSection("Source:MQTT:Sources").Get<IEnumerable<MqttSourceConfiguration>>();

      if (sources != null)
        foreach (var source in sources)
        {
          services.AddSingleton<IRadioSenderHost, MqttSource>(sp => new MqttSource(
            sp.GetRequiredService<FilterService>(),
            sp.GetRequiredService<DispatcherService>(),
            source));
        }
    });

    return builder;
  }
}

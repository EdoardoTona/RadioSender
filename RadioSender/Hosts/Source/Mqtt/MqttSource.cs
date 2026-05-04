using MQTTnet;
using MQTTnet.Protocol;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Protocol.Sportident;
using RadioSender.Hosts.Protocol.TmF;
using Serilog;
using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Mqtt;

public sealed class MqttSource(
  FilterService filterService,
  DispatcherService dispatcherService,
  MqttSourceConfiguration configuration) : ISource, IRadioSenderHost, IAsyncDisposable
{
  private readonly CancellationTokenSource _cts = new();
  private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
  private Task? _connectionTask;
  private bool _disposed;

  private string SourceId => string.IsNullOrWhiteSpace(configuration.SourceId)
    ? $"MQTT:{configuration.Host}"
    : configuration.SourceId!;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(configuration.Host))
    {
      Log.Warning("MQTT source ignored: Host is empty");
      return Task.CompletedTask;
    }

    if (!configuration.Topics.Any(t => !string.IsNullOrWhiteSpace(t)))
    {
      Log.Warning("MQTT source {source} ignored: no topic configured", SourceId);
      return Task.CompletedTask;
    }

    if (configuration.Protocols.Length == 0)
    {
      Log.Warning("MQTT source {source} ignored: no protocol configured", SourceId);
      return Task.CompletedTask;
    }

    _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceived;
    _connectionTask = RunAsync(_cts.Token);

    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    if (_disposed)
      return;

    _disposed = true;
    _cts.Cancel();

    if (_client.IsConnected)
    {
      try
      {
        await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
      }
      catch (Exception e)
      {
        Log.Warning(e, "Error disconnecting MQTT source {source}", SourceId);
      }
    }

    if (_connectionTask != null)
      await _connectionTask;

    _client.Dispose();
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync(default);
  }

  private async Task RunAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        if (!_client.IsConnected)
        {
          using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, configuration.TimeoutSeconds)));

          await _client.ConnectAsync(BuildOptions(), timeoutCts.Token);
          await SubscribeAsync(timeoutCts.Token);

          Log.Information("MQTT source {source} connected to {host}:{port}", SourceId, configuration.Host, configuration.Port);
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // quiet
      }
      catch (OperationCanceledException)
      {
        Log.Warning("MQTT source {source} connection timeout", SourceId);
        await DelayReconnect(cancellationToken);
      }
      catch (Exception e)
      {
        Log.Warning(e, "MQTT source {source} connection error", SourceId);
        await DelayReconnect(cancellationToken);
      }
    }
  }

  private MqttClientOptions BuildOptions()
  {
    var builder = new MqttClientOptionsBuilder()
      .WithProtocolVersion(configuration.ProtocolVersion)
      .WithCleanSession(configuration.CleanSession)
      .WithTimeout(TimeSpan.FromSeconds(Math.Max(1, configuration.TimeoutSeconds)))
      .WithKeepAlivePeriod(TimeSpan.FromSeconds(Math.Max(1, configuration.KeepAliveSeconds)));

    if (!string.IsNullOrWhiteSpace(configuration.ClientId))
      builder.WithClientId(configuration.ClientId);

    if (!string.IsNullOrWhiteSpace(configuration.Username))
      builder.WithCredentials(configuration.Username, configuration.Password);

    if (configuration.UseWebSocket)
      builder.WithWebSocketServer(options => options.WithUri(BuildWebSocketUri()));
    else
      builder.WithTcpServer(configuration.Host, configuration.Port);

    builder.WithTlsOptions(options => options.UseTls(configuration.UseTls));

    return builder.Build();
  }

  private string BuildWebSocketUri()
  {
    var scheme = configuration.UseTls ? "wss" : "ws";
    var port = configuration.Port.HasValue ? $":{configuration.Port.Value}" : string.Empty;
    var path = string.IsNullOrWhiteSpace(configuration.WebSocketPath)
      ? "/mqtt"
      : configuration.WebSocketPath!;

    if (!path.StartsWith('/'))
      path = "/" + path;

    return $"{scheme}://{configuration.Host}{port}{path}";
  }

  private async Task SubscribeAsync(CancellationToken cancellationToken)
  {
    var builder = new MqttClientSubscribeOptionsBuilder();

    foreach (var topic in configuration.Topics.Where(t => !string.IsNullOrWhiteSpace(t)))
    {
      builder.WithTopicFilter(topicBuilder =>
      {
        topicBuilder.WithTopic(topic);
        switch (configuration.QualityOfService)
        {
          case 1:
            topicBuilder.WithAtLeastOnceQoS();
            break;
          case 2:
            topicBuilder.WithExactlyOnceQoS();
            break;
          default:
            topicBuilder.WithAtMostOnceQoS();
            break;
        }
      });
    }

    await _client.SubscribeAsync(builder.Build(), cancellationToken);
    Log.Information("MQTT source {source} subscribed to {topics}", SourceId, string.Join(", ", configuration.Topics));
  }

  private Task OnApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs args)
  {
    try
    {
      var payload = args.ApplicationMessage.Payload.ToArray();
      if (payload.Length == 0)
        return Task.CompletedTask;

      foreach (var protocol in configuration.Protocols)
      {
        var dispatch = TryParse(protocol, payload, args.ApplicationMessage.Topic);
        if (dispatch == null)
          continue;

        dispatch = ApplyFilter(dispatch);
        if (dispatch != null)
          dispatcherService.PushDispatch(dispatch);

        return Task.CompletedTask;
      }

      Log.Warning("MQTT source {source} received unrecognized payload on {topic}: {hex}", SourceId, args.ApplicationMessage.Topic, Convert.ToHexString(payload));
    }
    catch (Exception e)
    {
      Log.Error(e, "MQTT source {source} error parsing message on {topic}", SourceId, args.ApplicationMessage.Topic);
    }

    return Task.CompletedTask;
  }

  private PunchDispatch? TryParse(MqttPayloadProtocol protocol, byte[] payload, string topic)
  {
    return protocol switch
    {
      MqttPayloadProtocol.Sportident => TryParseSportident(payload),
      MqttPayloadProtocol.TmF => TryParseTmF(payload, topic),
      _ => null
    };
  }

  private PunchDispatch? TryParseSportident(byte[] payload)
  {
    var punch = SportidentProtocol.MessageToPunch(payload, SourceId);
    return punch == null ? null : new PunchDispatch(Punches: [punch]);
  }

  private PunchDispatch? TryParseTmF(byte[] payload, string topic)
  {
    var dispatch = TmFProtocol.MessageToDispatch(payload, out var message, out var serialText, out var error);

    if (error != null)
      Log.Verbose("MQTT source {source} ignored invalid TmF payload on {topic}: {error}", SourceId, topic, error);
    else if (message is RxData packet && dispatch?.Punches == null && serialText != null)
      Log.Information("MQTT source {source} TmF source {tmfSource} says: {ascii}", SourceId, packet.Header.OrigID, serialText);

    return dispatch;
  }

  private PunchDispatch? ApplyFilter(PunchDispatch dispatch)
  {
    if (dispatch.Punches == null)
      return dispatch;

    var punches = filterService.Transform(configuration.Filter, dispatch.Punches).ToArray();
    if (punches.Length == 0 && dispatch.Hops == null && dispatch.Nodes == null)
      return null;

    return dispatch with { Punches = punches };
  }

  private async Task DelayReconnect(CancellationToken cancellationToken)
  {
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, configuration.ReconnectDelaySeconds)), cancellationToken);
    }
    catch (OperationCanceledException)
    {
      // quiet
    }
  }
}

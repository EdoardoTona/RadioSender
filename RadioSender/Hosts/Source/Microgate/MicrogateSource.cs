using Microgate.Common.Protocol.Rei2;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public abstract class MicrogateSource : ISource, IRadioSenderHost, IDisposable
{
  private readonly CancellationTokenSource _lifetime = new();
  private readonly object _sendLock = new();
  private readonly MicrogateMessageProcessor _messageProcessor;
  private bool _disposed;

  protected MicrogateSource(
    FilterService filterService,
    DispatcherService dispatcherService,
    MicrogateSourceConfiguration configuration,
    string endpoint)
  {
    Configuration = configuration;
    Endpoint = endpoint;
    _messageProcessor = new MicrogateMessageProcessor(
      filterService,
      dispatcherService,
      configuration,
      endpoint);
  }

  protected MicrogateSourceConfiguration Configuration { get; }
  protected string Endpoint { get; }
  protected CancellationToken LifetimeToken => _lifetime.Token;
  protected bool IsStopping { get; private set; }

  public abstract Task StartAsync(CancellationToken cancellationToken);
  public abstract Task StopAsync(CancellationToken cancellationToken);

  protected bool BeginStop()
  {
    if (IsStopping)
      return false;

    IsStopping = true;
    _lifetime.Cancel();
    return true;
  }

  protected void OnTransportConnected()
  {
    _messageProcessor.Reset();

    ObserveRequestFailure(AskSerialNumber(), nameof(AskSerialNumber));
    ObserveRequestFailure(AskRetransmission(), nameof(AskRetransmission));
  }

  protected void OnReceived(byte[] buffer, long offset, long size)
  {
    _messageProcessor.Receive(buffer.AsSpan((int)offset, (int)size));
  }

  protected abstract void SendCore(ReadOnlySpan<byte> data);

  protected virtual void DisposeTransport()
  {
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    DisposeTransport();
    _lifetime.Dispose();
    _disposed = true;
    GC.SuppressFinalize(this);
  }

  private async Task AskRetransmission()
  {
    await Task.Delay(1500, LifetimeToken).ConfigureAwait(false);

    Send(new Rei2StaticRequest
    {
      RequestingDevice = 'R',
      RequestId = 1,
      CompetitorNumber = 0,
      Info = InfoExtEnum.TimeOfDay,
      LogicalChannel = 251,
      Run = 1,
      Output = OutputStaticEnum.S
    }.Raw);
  }

  private async Task AskSerialNumber()
  {
    await Task.Delay(1000, LifetimeToken).ConfigureAwait(false);

    Send(new Rei2StatusRequest
    {
      StatusCode = 9999,
      RequestingDevice = 'R',
      RequestId = 1
    }.Raw);

    Send(new Rei2StatusRequest
    {
      StatusCode = 1000,
      RequestingDevice = 'R',
      RequestId = 2
    }.Raw);
  }

  private void Send(ReadOnlySpan<byte> data)
  {
    lock (_sendLock)
      SendCore(data);
  }

  private static void ObserveRequestFailure(Task request, string requestName)
  {
    _ = request.ContinueWith(
      task => Log.Warning("MicrogateSource {request} failed: {error}",
        requestName, task.Exception?.GetBaseException().Message),
      TaskContinuationOptions.OnlyOnFaulted);
  }
}

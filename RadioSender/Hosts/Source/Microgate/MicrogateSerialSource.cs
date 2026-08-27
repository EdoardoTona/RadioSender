using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.Microplus;

public sealed class MicrogateSerialSource : MicrogateSource
{
  private SerialPort? _port;
  private Task? _readTask;

  public MicrogateSerialSource(
    FilterService filterService,
    DispatcherService dispatcherService,
    MicrogateSourceConfiguration configuration)
    : base(filterService, dispatcherService, configuration, GetEndpoint(configuration))
  {
  }

  public override Task StartAsync(CancellationToken cancellationToken)
  {
    Log.Information("MicrogateSource opening serial port {port} at {baudrate} baud",
      Endpoint, Configuration.Baudrate);
    _readTask = Task.Run(() => RunAsync(LifetimeToken), CancellationToken.None);
    return Task.CompletedTask;
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    if (!BeginStop())
      return;

    ClosePort();

    if (_readTask == null)
      return;

    try
    {
      await _readTask.WaitAsync(cancellationToken);
    }
    catch (OperationCanceledException) when (LifetimeToken.IsCancellationRequested)
    {
      // Normal shutdown.
    }
  }

  protected override void SendCore(ReadOnlySpan<byte> data)
  {
    var port = _port;
    if (port == null || !port.IsOpen)
      throw new IOException($"Serial port {Endpoint} is not connected.");

    var bytes = data.ToArray();
    port.Write(bytes, 0, bytes.Length);
  }

  private async Task RunAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      SerialPort? port = null;
      try
      {
        port = CreatePort();
        port.Open();
        _port = port;

        Log.Information(
          "MicrogateSource serial port {port} connected at {baudrate} baud (DTR: {dtr}, RTS: {rts})",
          Endpoint, Configuration.Baudrate, Configuration.DtrEnable, Configuration.RtsEnable);
        OnTransportConnected();

        Read(port, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception) when (cancellationToken.IsCancellationRequested)
      {
        // Closing SerialPort is also used to unblock a pending read during shutdown.
        break;
      }
      catch (UnauthorizedAccessException)
      {
        Log.Warning("MicrogateSource serial port {port} is occupied by another program; retrying", Endpoint);
      }
      catch (Exception)
      {
        Log.Warning("MicrogateSource serial port {port} disconnected or unavailable; retrying", Endpoint);
      }
      finally
      {
        if (ReferenceEquals(_port, port))
          _port = null;

        DisposePort(port);
      }

      await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
    }
  }

  private SerialPort CreatePort() => new(
    Configuration.PortName!,
    Configuration.Baudrate,
    Parity.None,
    8,
    StopBits.One)
  {
    Handshake = Handshake.None,
    DtrEnable = Configuration.DtrEnable,
    RtsEnable = Configuration.RtsEnable,
    // Reads run on a worker thread; a short timeout lets shutdown and reconnects
    // react promptly without sizing buffers for DeviceView's large image payloads.
    ReadTimeout = 500,
    WriteTimeout = 1000
  };

  private void Read(SerialPort port, CancellationToken cancellationToken)
  {
    var buffer = new byte[4096];
    while (!cancellationToken.IsCancellationRequested && port.IsOpen)
    {
      int read;
      try
      {
        read = port.Read(buffer, 0, buffer.Length);
      }
      catch (TimeoutException)
      {
        continue;
      }

      if (read > 0)
        OnReceived(buffer, 0, read);
    }
  }

  private static async Task DelayBeforeReconnect(CancellationToken cancellationToken)
  {
    if (cancellationToken.IsCancellationRequested)
      return;

    try
    {
      await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // Normal shutdown.
    }
  }

  private void ClosePort()
  {
    var port = _port;
    if (port == null)
      return;

    try
    {
      if (port.IsOpen)
        port.Close();
    }
    catch (Exception e)
    {
      Log.Debug(e, "MicrogateSource error while closing serial port {port}", Endpoint);
    }
  }

  private static void DisposePort(SerialPort? port)
  {
    if (port == null)
      return;

    try
    {
      if (port.IsOpen)
        port.Close();
    }
    catch
    {
      // Best effort while reconnecting or shutting down.
    }

    port.Dispose();
  }

  private static string GetEndpoint(MicrogateSourceConfiguration configuration)
  {
    if (string.IsNullOrWhiteSpace(configuration.PortName))
      throw new ArgumentException("A serial Microgate source requires PortName.", nameof(configuration));

    if (!string.IsNullOrWhiteSpace(configuration.Address) || configuration.Port.HasValue)
      throw new ArgumentException("A serial Microgate source cannot define Address or Port.", nameof(configuration));

    if (configuration.Baudrate <= 0)
      throw new ArgumentOutOfRangeException(nameof(configuration), "Baudrate must be greater than zero.");

    return configuration.PortName;
  }
}

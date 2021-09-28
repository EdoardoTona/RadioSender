using Microsoft.Extensions.Hosting;
using PhotinoNET;
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Liveresults.UI
{
  public class PhotinoHostedService : IHostedService
  {
    private readonly string _port;
    private readonly bool _isDevelopment;

    private static Action? _terminatePhotinoAction;
    private static Action? _terminateAppAction;

    private Thread? _thread;


    public PhotinoHostedService(
      string urls,
      IHostApplicationLifetime hostApplicationLifetime,
      IHostEnvironment hostEnvironment
      )
    {
      _terminateAppAction = () => hostApplicationLifetime?.StopApplication();
      _port = Regex.Match(urls, @"(?<=:)\d{2,5}").Value;
      _isDevelopment = hostEnvironment.IsDevelopment();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _thread = new Thread(new ParameterizedThreadStart(PhotinoThread));

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        _thread.SetApartmentState(ApartmentState.STA);

      _thread.Start(new { Port = _port, IsDevelopment = _isDevelopment });

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      if (_thread != null && _thread.IsAlive)
        _terminatePhotinoAction?.Invoke();

      return Task.CompletedTask;
    }

    static void PhotinoThread(object? param)
    {
      if (param == null)
        return;

      dynamic p = param;

      var port = (string)p.Port;
      var isDevelopment = (bool)p.IsDevelopment;

      var iconFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
          ? "wwwroot/favicon.ico"
          : "wwwroot/favicon.png";

      var window = new PhotinoWindow()
        .SetIconFile(iconFile)
        .SetTitle("CIS")
        .SetChromeless(false)
        .SetUseOsDefaultSize(true)
        .SetResizable(true)
        .Center()
        .Load($"http://127.0.0.1:{port}/");

      _terminatePhotinoAction = () => window?.Close();

      window.WaitForClose();

      _terminateAppAction?.Invoke();
    }

  }
}

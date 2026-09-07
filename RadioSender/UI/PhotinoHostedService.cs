using Microsoft.Extensions.Hosting;
using Photino.NET;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.UI
{
  public class PhotinoHostedService : IHostedService
  {
    private readonly string _port;

    private static Action? _terminatePhotinoAction;
    private static Action? _terminateAppAction;
    private static volatile bool _isTerminating;
    private static bool _flowEditorReady;
    private static bool _closeRequested;

    private Thread? _thread;


    public PhotinoHostedService(
      string urls,
      IHostApplicationLifetime hostApplicationLifetime
      )
    {
      _terminateAppAction = () => hostApplicationLifetime?.StopApplication();
      _port = Regex.Match(urls, @"(?<=:)\d{2,5}").Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      // On macOS, Photino runs on the main thread (handled by Program.cs),
      // so we only need to start a separate thread on Windows/Linux.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return Task.CompletedTask;

      _thread = new Thread(() => RunPhotinoWindow(_port));

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        _thread.SetApartmentState(ApartmentState.STA);

      _thread.Start();

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      if (_thread != null && _thread.IsAlive)
        _terminatePhotinoAction?.Invoke();

      return Task.CompletedTask;
    }

    /// <summary>
    /// Runs Photino on the main thread (required for macOS).
    /// Called directly from Program.cs on macOS.
    /// </summary>
    public static void RunOnMainThread(string port, Action stopApplication)
    {
      _terminateAppAction = stopApplication;
      RunPhotinoWindow(port);
    }

    public static void StopWindow()
    {
      _terminatePhotinoAction?.Invoke();
    }

    private static string ResolveIcon()
    {
      var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "favicon.ico" : "favicon.png";

      var diskPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
      if (File.Exists(diskPath))
        return diskPath;

      // Single-file publish: extract from embedded resource
      var tempPath = Path.Combine(Path.GetTempPath(), $"radiosender_{fileName}");
      if (!File.Exists(tempPath))
      {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"RadioSender.wwwroot.{fileName}");
        if (stream != null)
        {
          using var dest = File.Create(tempPath);
          stream.CopyTo(dest);
        }
      }
      return tempPath;
    }

    private static void RunPhotinoWindow(string port)
    {
      var iconFile = ResolveIcon();

      var window = new PhotinoWindow()
        .SetIconFile(iconFile)
        .SetTitle("RadioSender")
        .SetChromeless(false)
        .SetDevToolsEnabled(true)
        .SetUseOsDefaultSize(false)
        .SetSize(new System.Drawing.Size(1440, 950))
        .SetResizable(true)
        .Center()
        .RegisterWebMessageReceivedHandler(HandleWebMessage)
        .Load($"http://127.0.0.1:{port}/flows/index.html")
        .RegisterWindowClosingHandler(new PhotinoWindow.NetClosingDelegate(Window_WindowClosing));

      _terminatePhotinoAction = () => { _isTerminating = true; window?.Close(); };

      window.WaitForClose();

      _terminateAppAction?.Invoke();
    }

    private static bool Window_WindowClosing(object sender, EventArgs e)
    {
      // Skip confirmation dialog if app is already terminating (avoids macOS NSInternalInconsistencyException)
      if (_isTerminating)
        return false;

      var window = sender as PhotinoWindow;

      if (_flowEditorReady && window != null)
      {
        _closeRequested = true;
        window.SendWebMessage("{\"kind\":\"closing\"}");
        return true;
      }

      var res = window?.ShowMessage("Radiosender", "Do you want to close?", PhotinoDialogButtons.YesNo, PhotinoDialogIcon.Warning);

      if (res != null && res == PhotinoDialogResult.Yes)
        return false;

      return true;
    }

    private static void HandleWebMessage(object? sender, string message)
    {
      if (sender is not PhotinoWindow window) return;
      string? requestId = null;
      try
      {
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        var kind = root.GetProperty("kind").GetString();
        if (kind == "flow-editor-ready") { _flowEditorReady = true; return; }
        if (kind == "flow-editor-leaving") { _flowEditorReady = false; return; }
        if (kind == "close-cancel") { _closeRequested = false; return; }
        if (kind == "close-ready" && _closeRequested)
        { _isTerminating = true; window.Close(); return; }
        if (kind != "file-dialog") return;
        requestId = root.GetProperty("id").GetString();
        var defaultPath = root.GetProperty("path").GetString();
        var mode = root.GetProperty("mode").GetString();
        (string Name, string[] Extensions)[] filters = [("RadioSender configuration", ["json"])];
        var path = mode == "open"
          ? window.ShowOpenFile("Open configuration", defaultPath, false, filters).FirstOrDefault()
          : window.ShowSaveFile("Save configuration", defaultPath, filters);
        window.SendWebMessage(JsonSerializer.Serialize(new { kind = "file-dialog-result", id = requestId, path }));
      }
      catch (Exception ex)
      {
        if (requestId != null)
          window.SendWebMessage(JsonSerializer.Serialize(new { kind = "file-dialog-result", id = requestId, error = ex.Message }));
      }
    }
  }
}

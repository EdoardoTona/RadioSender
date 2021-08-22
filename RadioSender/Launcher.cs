using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RadioSender
{
  public sealed class Launcher : IHostedService, IDisposable
  {
    private static readonly string URL = "http://localhost:5000/";

    private TaskbarIcon? taskBar;
    private readonly IHostApplicationLifetime appLife;
    private readonly IWebHostEnvironment env;

    private Thread? _uiThread;

    public Launcher(IHostApplicationLifetime appLife, IWebHostEnvironment env)
    {
      this.appLife = appLife;
      this.env = env;
    }

    public void Dispose()
    {
      taskBar?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        _ = ShowTrayIcon(cancellationToken);

      if (!env.IsDevelopment())
        OpenBrowser(URL);

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      taskBar?.Dispose();

      if (_uiThread != null)
        Dispatcher.FromThread(_uiThread).InvokeShutdown();

      return Task.CompletedTask;
    }

    private Task ShowTrayIcon(CancellationToken cancellationToken)
    {
      _uiThread = new Thread(new ThreadStart(() =>
      {
        try
        {
          var menu = new ContextMenu();
          var mi = new MenuItem()
          {
            Header = "Radiosender",
            Icon = SystemIcons.Application
          };
          mi.Click += (object sender, RoutedEventArgs e) => OpenInfoWindow();
          menu.Items.Add(mi);
          mi = new MenuItem()
          {
            Header = "Apri browser",
          };
          mi.Click += (object sender, RoutedEventArgs e) => OpenBrowser(URL);
          menu.Items.Add(mi);
          mi = new MenuItem()
          {
            Header = "Esci",
          };
          mi.Click += (object sender, RoutedEventArgs e) =>
          {
            //var res = MessageBox.Show(
            //  "Sicuro di voler chiudere Radiosender?",
            //  "Oribos.Server",
            //  MessageBoxButton.YesNo,
            //  MessageBoxImage.Question,
            //  MessageBoxResult.No,
            //  MessageBoxOptions.DefaultDesktopOnly);
            //if (res == MessageBoxResult.Yes)
            //{
            //  Quit();
            //}
            Quit();
          };
          menu.Items.Add(mi);


          taskBar = new TaskbarIcon
          {
            ToolTipText = "Radiosender",
            Icon = SystemIcons.Application,
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.LeftOrRightClick,
          };
          taskBar.TrayBalloonTipClicked += (object sender, RoutedEventArgs e) => OpenBrowser(URL);
          taskBar.ShowBalloonTip("Server avviato", URL, SystemIcons.Information, true);

          Dispatcher.Run();
        }
        catch { }
      }));
      _uiThread.SetApartmentState(ApartmentState.STA);
      _uiThread.IsBackground = true;
      _uiThread.Start();

      return Task.CompletedTask;
    }

    private static void OpenInfoWindow()
    {

      var w = new Window
      {
        Width = 400,
        Height = 300,
        Title = "Radiosender",
        WindowStyle = WindowStyle.ToolWindow,
        ResizeMode = ResizeMode.NoResize,
      };
      //using (var memory = new MemoryStream())
      //{
      //  var img = Properties.Resources.logo_oribos_solo_img.ToBitmap();
      //  img.Save(memory, ImageFormat.Bmp);
      //  memory.Position = 0;

      //  var bitmapImage = new BitmapImage();
      //  bitmapImage.BeginInit();
      //  bitmapImage.StreamSource = memory;
      //  bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
      //  bitmapImage.EndInit();

      //  var sk = new Grid();
      //  w.Content = sk;
      //  var im = new Image();
      //  im.Source = bitmapImage;
      //  sk.Children.Add(im);
      //}



      w.Show();

      var helper = new WindowInteropHelper(w); //this being the wpf form 
      var currentScreen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
      double factor = PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice.M11;

      w.Left = currentScreen.WorkingArea.Width / factor - w.Width;
      w.Top = currentScreen.WorkingArea.Height / factor - w.Height;
    }

    private void Quit()
    {
      appLife.StopApplication();
    }

    private static void OpenBrowser(string url)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
      }
      else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        Process.Start("xdg-open", url);
      }
      else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        Process.Start("open", url);
      }
      else
      {
        // throw 
      }
    }

  }
}
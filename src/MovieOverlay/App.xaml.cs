using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MovieOverlay.Services;
using MovieOverlay.Views;
using Application = System.Windows.Application;

namespace MovieOverlay;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppController? _controller;
    private NotifyIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CursorHider.RegisterExitHooks();
        CursorHider.RestoreIfFlagPresent();

        _mutex = new Mutex(true, @"Local\MovieOverlay.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Movie Overlay is already running (system tray).",
                "Movie Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _controller = new AppController();
        _controller.StateChanged += (_, _) => UpdateTrayText();
        _controller.UiStayOnTopRequested += (_, stayOnTop) =>
        {
            _settingsWindow?.Dispatcher.Invoke(() => _settingsWindow.SetStayOnTop(stayOnTop));
        };

        _tray = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "Movie Overlay",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Start / Stop watching", null, (_, _) => _controller.ToggleWatching());
        menu.Items.Add("Toggle blackout now", null, (_, _) => _controller.ToggleOverlayNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();

        UpdateTrayText();
        ShowSettings();
    }

    private void ShowSettings()
    {
        if (_controller is null)
        {
            return;
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            if (_controller.IsBlackoutActive)
            {
                // During blackout, do not raise the GUI above overlays.
                return;
            }

            _settingsWindow.Show();
            _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_controller);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void UpdateTrayText()
    {
        if (_tray is null || _controller is null)
        {
            return;
        }

        var state = _controller.IsBlackoutActive
            ? "blackout ON"
            : _controller.IsWatching
                ? "watching mouse"
                : "stopped";
        _tray.Text = $"Movie Overlay — {state}";
    }

    private void ExitApp()
    {
        CursorHider.ForceRestoreSystemCursors();
        _tray!.Visible = false;
        _controller?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CursorHider.ForceRestoreSystemCursors();
        _tray?.Dispose();
        _controller?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignored
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    private static Icon CreateTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icoPath))
        {
            return new Icon(icoPath);
        }

        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0x0D, 0x94, 0x88));
        using var brush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 7, System.Drawing.FontStyle.Bold);
        g.DrawString("M", font, brush, 2, 1);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}

using System.Windows.Threading;
using MovieOverlay.Models;

namespace MovieOverlay.Services;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DisplayService _displayService = new();
    private readonly OverlayManager _overlayManager = new();
    private readonly BrightnessService _brightnessService = new();
    private readonly MouseIdleWatcher _idleWatcher = new();
    private readonly DispatcherTimer _topmostTimer;
    private AppSettings _settings;
    private bool _enabled;
    private bool _blackoutActive;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public event EventHandler? Tick;
    /// <summary>When true, keep settings above overlays; when false, allow overlays to cover the GUI.</summary>
    public event EventHandler<bool>? UiStayOnTopRequested;

    public AppSettings Settings => _settings;
    public DisplayService Displays => _displayService;
    public bool IsOverlayVisible => _blackoutActive;
    public bool IsBlackoutActive => _blackoutActive;
    public bool IsEnabled => _enabled;
    public bool IsWatching => _idleWatcher.IsRunning;
    public double SecondsUntilIdle => _idleWatcher.SecondsUntilIdle;
    public bool IsIdle => _idleWatcher.IsIdle;

    public AppController()
    {
        _settings = _settingsStore.Load();
        ApplyWatcherSettings();
        _overlayManager.ClickThrough = _settings.ClickThrough;

        _displayService.DisplaysChanged += OnDisplaysChanged;
        _idleWatcher.BecameIdle += OnBecameIdle;
        _idleWatcher.BecameActive += OnBecameActive;
        _idleWatcher.MouseMoved += OnMouseMoved;
        _idleWatcher.Tick += (_, e) => Tick?.Invoke(this, e);

        _topmostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _topmostTimer.Tick += (_, _) =>
        {
            if (_blackoutActive)
            {
                // Keep overlays above the settings window.
                _overlayManager.BringToFront();
            }
        };

        SyncOverlayTargets();
        StartupHelper.SetStartWithWindows(_settings.StartWithWindows);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _settings.IdleSeconds = Math.Clamp(_settings.IdleSeconds, 1, 3600);
        _settings.MovementThresholdPx = Math.Clamp(_settings.MovementThresholdPx, 0, 200);
        _settingsStore.Save(_settings);

        ApplyWatcherSettings();
        _overlayManager.ClickThrough = _settings.ClickThrough;
        if (!_blackoutActive)
        {
            SyncOverlayTargets();
        }

        StartupHelper.SetStartWithWindows(_settings.StartWithWindows);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartWatching()
    {
        _enabled = true;
        ClearBlackout();
        ApplyWatcherSettings();
        _idleWatcher.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopWatching()
    {
        _enabled = false;
        _idleWatcher.Stop();
        ClearBlackout();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleWatching()
    {
        if (_enabled && _idleWatcher.IsRunning)
        {
            StopWatching();
        }
        else
        {
            StartWatching();
        }
    }

    public void ToggleOverlayNow()
    {
        if (_blackoutActive)
        {
            ClearBlackout();
        }
        else
        {
            ApplyBlackout();
        }
    }

    private void ApplyWatcherSettings()
    {
        _idleWatcher.IdleThreshold = TimeSpan.FromSeconds(Math.Clamp(_settings.IdleSeconds, 1, 3600));
        _idleWatcher.MovementThresholdPixels = Math.Clamp(_settings.MovementThresholdPx, 0, 200);
    }

    private void OnBecameIdle(object? sender, EventArgs e)
    {
        if (!_enabled || _settings.SelectedDeviceNames.Count == 0)
        {
            return;
        }

        ApplyBlackout();
    }

    private void OnBecameActive(object? sender, EventArgs e) => ClearBlackout();

    private void OnMouseMoved(object? sender, EventArgs e)
    {
        if (_blackoutActive)
        {
            ClearBlackout();
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_blackoutActive)
        {
            ClearBlackout();
        }
        else
        {
            SyncOverlayTargets();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<MonitorInfo> GetSelectedMonitors()
    {
        if (!_settingsStore.HadExistingFile && _settings.SelectedDeviceNames.Count == 0)
        {
            var defaults = _displayService.Monitors.Where(m => !m.IsPrimary).ToList();
            if (defaults.Count == 0)
            {
                defaults = _displayService.Monitors.ToList();
            }

            _settings.SelectedDeviceNames = defaults.Select(m => m.DeviceName).ToList();
            _settingsStore.Save(_settings);
        }

        return _displayService.Monitors
            .Where(m => _settings.SelectedDeviceNames.Contains(m.DeviceName, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private void SyncOverlayTargets()
    {
        _overlayManager.SyncTargets(GetSelectedMonitors());
    }

    private void ApplyBlackout()
    {
        var targets = GetSelectedMonitors();
        if (targets.Count == 0)
        {
            ClearBlackout();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Always paint overlays on every selected monitor so the GUI is covered too.
        // Hardware dim is optional extra; overlay ensures the settings window is hidden.
        var overlayTargets = targets.ToList();

        if (_settings.PreferHardwareBrightness)
        {
            foreach (var monitor in targets)
            {
                _brightnessService.TryDimToZero(monitor);
            }
        }

        // Drop settings window below overlays.
        UiStayOnTopRequested?.Invoke(this, false);

        _overlayManager.SyncTargets(overlayTargets);
        _overlayManager.Show();
        _topmostTimer.Start();

        CursorHider.Hide();
        _blackoutActive = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearBlackout()
    {
        _topmostTimer.Stop();
        _overlayManager.Hide();
        _brightnessService.RestoreAll();
        CursorHider.Show();
        _blackoutActive = false;
        UiStayOnTopRequested?.Invoke(this, false);
        SyncOverlayTargets();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _topmostTimer.Stop();
        CursorHider.ForceRestoreSystemCursors();
        _idleWatcher.Dispose();
        _overlayManager.Dispose();
        _brightnessService.Dispose();
        _displayService.Dispose();
    }
}

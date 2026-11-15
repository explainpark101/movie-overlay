using System.Windows;
using System.Windows.Media;
using MovieOverlay.Models;
using MovieOverlay.Services;
using Color = System.Windows.Media.Color;

namespace MovieOverlay.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Loaded += OnLoaded;
        _controller.StateChanged += OnControllerStateChanged;
        _controller.Tick += OnControllerTick;
        Closed += (_, _) =>
        {
            _controller.StateChanged -= OnControllerStateChanged;
            _controller.Tick -= OnControllerTick;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReloadFromController();
        UpdateStatusText();
    }

    private void OnControllerStateChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            UpdateStartStopButton();
            UpdateStatusText();
        });
    }

    private void OnControllerTick(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.Invoke(UpdateStatusText);
    }

    private void ReloadFromController()
    {
        var settings = _controller.Settings;
        MonitorMap.SetMonitors(_controller.Displays.Monitors, settings.SelectedDeviceNames);
        IdleSecondsBox.Text = settings.IdleSeconds.ToString();
        MovementThresholdBox.Text = settings.MovementThresholdPx.ToString();
        PreferHardwareBrightnessBox.IsChecked = settings.PreferHardwareBrightness;
        ClickThroughBox.IsChecked = settings.ClickThrough;
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
        UpdateStartStopButton();
        UpdateStatusText();
    }

    public void SetStayOnTop(bool stayOnTop)
    {
        Topmost = stayOnTop;
        if (!stayOnTop)
        {
            return;
        }

        if (IsLoaded)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void UpdateStartStopButton()
    {
        if (_controller.IsWatching)
        {
            StartStopButton.Content = "Stop";
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
        }
        else
        {
            StartStopButton.Content = "Start";
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88));
        }
    }

    private void UpdateStatusText()
    {
        if (!_controller.IsWatching)
        {
            StatusText.Text = "Stopped";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3));
            return;
        }

        if (_controller.IsBlackoutActive || _controller.IsIdle)
        {
            StatusText.Text = "Blackout active — move mouse to restore";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            return;
        }

        var seconds = Math.Ceiling(_controller.SecondsUntilIdle);
        StatusText.Text = $"Blackout in {seconds:0}s";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
    }

    private bool TryReadSettings(out AppSettings settings)
    {
        settings = new AppSettings();
        if (!int.TryParse(IdleSecondsBox.Text.Trim(), out var idle) || idle < 1)
        {
            System.Windows.MessageBox.Show(
                this,
                "Idle seconds must be a whole number of at least 1.",
                "Movie Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(MovementThresholdBox.Text.Trim(), out var movePx) || movePx < 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Movement threshold must be a whole number of at least 0.",
                "Movie Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        settings = new AppSettings
        {
            IdleSeconds = idle,
            MovementThresholdPx = movePx,
            SelectedDeviceNames = MonitorMap.SelectedDeviceNames.ToList(),
            PreferHardwareBrightness = PreferHardwareBrightnessBox.IsChecked == true,
            ClickThrough = ClickThroughBox.IsChecked == true,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
        };
        return true;
    }

    private void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_controller.IsWatching)
        {
            _controller.StopWatching();
            UpdateStartStopButton();
            UpdateStatusText();
            return;
        }

        if (!TryReadSettings(out var settings))
        {
            return;
        }

        if (settings.SelectedDeviceNames.Count == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Select at least one display to black out.",
                "Movie Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _controller.ApplySettings(settings);
        _controller.StartWatching();
        UpdateStartStopButton();
        UpdateStatusText();
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings))
        {
            return;
        }

        _controller.ApplySettings(settings);
        UpdateStatusText();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

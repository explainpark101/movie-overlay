using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MovieOverlay.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace MovieOverlay.Controls;

public partial class MonitorMapControl : UserControl
{
    private static readonly Brush BlackoutFill = CreateFrozenBrush(0x11, 0x11, 0x11);
    private static readonly Brush BlackoutStroke = CreateFrozenBrush(0x3F, 0x3F, 0x46);
    private static readonly Brush KeepFill = CreateFrozenBrush(0x0D, 0x94, 0x88);
    private static readonly Brush KeepStroke = CreateFrozenBrush(0x2D, 0xD4, 0xBF);
    private static readonly Brush LabelBrush = CreateFrozenBrush(0xF5, 0xF5, 0xF5);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private IReadOnlyList<MonitorInfo> _monitors = [];
    private HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? SelectionChanged;

    public IReadOnlyCollection<string> SelectedDeviceNames => _selected;

    public MonitorMapControl()
    {
        InitializeComponent();
    }

    public void SetMonitors(IReadOnlyList<MonitorInfo> monitors, IEnumerable<string> selectedDeviceNames)
    {
        _monitors = monitors;
        _selected = new HashSet<string>(selectedDeviceNames, StringComparer.OrdinalIgnoreCase);
        Redraw();
    }

    public void SetSelection(IEnumerable<string> selectedDeviceNames)
    {
        _selected = new HashSet<string>(selectedDeviceNames, StringComparer.OrdinalIgnoreCase);
        Redraw();
    }

    private void MapCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        MapCanvas.Children.Clear();

        if (_monitors.Count == 0 || MapCanvas.ActualWidth <= 0 || MapCanvas.ActualHeight <= 0)
        {
            return;
        }

        var minX = _monitors.Min(m => m.Left);
        var minY = _monitors.Min(m => m.Top);
        var maxX = _monitors.Max(m => m.Left + m.Width);
        var maxY = _monitors.Max(m => m.Top + m.Height);

        var worldW = Math.Max(1, maxX - minX);
        var worldH = Math.Max(1, maxY - minY);

        const double padding = 16;
        var availW = Math.Max(1, MapCanvas.ActualWidth - padding * 2);
        var availH = Math.Max(1, MapCanvas.ActualHeight - padding * 2);
        var scale = Math.Min(availW / worldW, availH / worldH);

        var offsetX = padding + (availW - worldW * scale) / 2;
        var offsetY = padding + (availH - worldH * scale) / 2;

        foreach (var monitor in _monitors)
        {
            var blackout = _selected.Contains(monitor.DeviceName);
            var x = offsetX + (monitor.Left - minX) * scale;
            var y = offsetY + (monitor.Top - minY) * scale;
            var w = Math.Max(24, monitor.Width * scale);
            var h = Math.Max(16, monitor.Height * scale);

            var rect = new Rectangle
            {
                Width = w,
                Height = h,
                Fill = blackout ? BlackoutFill : KeepFill,
                Stroke = blackout ? BlackoutStroke : KeepStroke,
                StrokeThickness = blackout ? 2.5 : 1.5,
                RadiusX = 4,
                RadiusY = 4,
                Cursor = Cursors.Hand,
                Tag = monitor.DeviceName,
                ToolTip = monitor.DisplayLabel,
            };

            rect.MouseLeftButtonUp += Monitor_OnClick;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            MapCanvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = monitor.DisplayNumber.ToString(),
                Foreground = LabelBrush,
                FontSize = Math.Clamp(Math.Min(w, h) * 0.35, 14, 28),
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };

            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelW = label.DesiredSize.Width;
            var labelH = label.DesiredSize.Height;
            Canvas.SetLeft(label, x + (w - labelW) / 2);
            Canvas.SetTop(label, y + (h - labelH) / 2);
            MapCanvas.Children.Add(label);
        }
    }

    private void Monitor_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string deviceName })
        {
            return;
        }

        if (!_selected.Add(deviceName))
        {
            _selected.Remove(deviceName);
        }

        Redraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

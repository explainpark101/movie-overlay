using System.Runtime.InteropServices;
using MovieOverlay.Models;
using MovieOverlay.Native;
using Microsoft.Win32;

namespace MovieOverlay.Services;

public sealed class DisplayService : IDisposable
{
    private readonly List<MonitorInfo> _monitors = [];
    private bool _disposed;

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    public DisplayService()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Refresh();
    }

    public void Refresh()
    {
        _monitors.Clear();

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            EnumCallback,
            IntPtr.Zero);

        _monitors.Sort((a, b) =>
        {
            var cmp = a.Left.CompareTo(b.Left);
            return cmp != 0 ? cmp : a.Top.CompareTo(b.Top);
        });

        // Number like Windows Settings: primary = 1, then left-to-right / top-to-bottom.
        var ordered = _monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Left)
            .ThenBy(m => m.Top)
            .ToList();

        _monitors.Clear();
        for (var i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            _monitors.Add(new MonitorInfo
            {
                DisplayNumber = i + 1,
                DeviceName = m.DeviceName,
                Handle = m.Handle,
                Left = m.Left,
                Top = m.Top,
                Width = m.Width,
                Height = m.Height,
                IsPrimary = m.IsPrimary,
            });
        }

        // Keep layout order (geometry) for the map; numbers already assigned.
        _monitors.Sort((a, b) =>
        {
            var cmp = a.Left.CompareTo(b.Left);
            return cmp != 0 ? cmp : a.Top.CompareTo(b.Top);
        });
    }

    private int EnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData)
    {
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            return 1;
        }

        _monitors.Add(new MonitorInfo
        {
            DisplayNumber = 0,
            DeviceName = (info.szDevice ?? string.Empty).TrimEnd('\0').Trim(),
            Handle = hMonitor,
            Left = info.rcMonitor.Left,
            Top = info.rcMonitor.Top,
            Width = info.rcMonitor.Width,
            Height = info.rcMonitor.Height,
            IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
        });

        return 1;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Refresh();
        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}

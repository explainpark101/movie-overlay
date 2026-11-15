using MovieOverlay.Models;
using MovieOverlay.Native;

namespace MovieOverlay.Services;

/// <summary>
/// Dim monitors via DDC/CI (dxva2) when the hardware supports it.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private readonly Dictionary<string, SavedState> _saved = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public bool IsDimmed => _saved.Count > 0;

    /// <summary>
    /// Tries to set hardware brightness to 0. Returns true if at least one physical monitor dimmed.
    /// </summary>
    public bool TryDimToZero(MonitorInfo monitor)
    {
        if (_disposed)
        {
            return false;
        }

        // Already dimmed this logical display.
        if (_saved.ContainsKey(monitor.DeviceName))
        {
            return true;
        }

        uint count = 0;
        if (!DxvaMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, ref count) || count == 0)
        {
            return false;
        }

        var physical = new DxvaMethods.PHYSICAL_MONITOR[count];
        if (!DxvaMethods.GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, physical))
        {
            return false;
        }

        var entries = new List<PhysicalBrightness>();
        var any = false;

        try
        {
            foreach (var pm in physical)
            {
                if (pm.hPhysicalMonitor == IntPtr.Zero)
                {
                    continue;
                }

                uint min = 0, current = 0, max = 0;
                if (!DxvaMethods.GetMonitorBrightness(pm.hPhysicalMonitor, ref min, ref current, ref max))
                {
                    continue;
                }

                if (!DxvaMethods.SetMonitorBrightness(pm.hPhysicalMonitor, min))
                {
                    // Some panels only accept 0.
                    if (!DxvaMethods.SetMonitorBrightness(pm.hPhysicalMonitor, 0))
                    {
                        continue;
                    }
                }

                entries.Add(new PhysicalBrightness(pm.hPhysicalMonitor, min, current, max));
                any = true;
            }
        }
        finally
        {
            // Keep physical handles alive while dimmed; destroy on restore.
            if (!any)
            {
                DxvaMethods.DestroyPhysicalMonitors(count, physical);
            }
        }

        if (!any)
        {
            return false;
        }

        _saved[monitor.DeviceName] = new SavedState(physical, entries);
        return true;
    }

    public void RestoreAll()
    {
        foreach (var key in _saved.Keys.ToList())
        {
            Restore(key);
        }
    }

    public void Restore(string deviceName)
    {
        if (!_saved.Remove(deviceName, out var state))
        {
            return;
        }

        foreach (var entry in state.Entries)
        {
            try
            {
                DxvaMethods.SetMonitorBrightness(entry.Handle, entry.Current);
            }
            catch
            {
                // best effort
            }
        }

        try
        {
            DxvaMethods.DestroyPhysicalMonitors((uint)state.Physical.Length, state.Physical);
        }
        catch
        {
            // best effort
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RestoreAll();
    }

    private sealed record PhysicalBrightness(IntPtr Handle, uint Min, uint Current, uint Max);

    private sealed class SavedState
    {
        public SavedState(DxvaMethods.PHYSICAL_MONITOR[] physical, List<PhysicalBrightness> entries)
        {
            Physical = physical;
            Entries = entries;
        }

        public DxvaMethods.PHYSICAL_MONITOR[] Physical { get; }
        public List<PhysicalBrightness> Entries { get; }
    }
}

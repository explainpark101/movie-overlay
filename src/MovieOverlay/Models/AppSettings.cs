namespace MovieOverlay.Models;

public sealed class AppSettings
{
    public int IdleSeconds { get; set; } = 5;
    /// <summary>Cursor must move at least this many pixels to count as activity.</summary>
    public int MovementThresholdPx { get; set; } = 5;
    public List<string> SelectedDeviceNames { get; set; } = [];
    public bool StartWithWindows { get; set; }
    public bool ClickThrough { get; set; } = true;
    /// <summary>Prefer DDC/CI brightness 0; fall back to black overlay when unsupported.</summary>
    public bool PreferHardwareBrightness { get; set; } = true;
}

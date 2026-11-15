namespace MovieOverlay.Models;

public sealed class MonitorInfo
{
    public required int DisplayNumber { get; init; }
    public required string DeviceName { get; init; }
    public required IntPtr Handle { get; init; }
    public required int Left { get; init; }
    public required int Top { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool IsPrimary { get; init; }

    public string DisplayLabel =>
        IsPrimary ? $"Display {DisplayNumber} (Primary)" : $"Display {DisplayNumber}";
}

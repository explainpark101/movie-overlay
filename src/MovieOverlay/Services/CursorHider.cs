using System.Diagnostics;
using System.IO;
using MovieOverlay.Native;

namespace MovieOverlay.Services;

/// <summary>
/// Hides the mouse pointer system-wide by replacing system cursors with a blank cursor.
/// A watchdog child process restores cursors if this process is killed.
/// </summary>
internal static class CursorHider
{
    private static readonly uint[] SystemCursorIds =
    [
        CursorMethods.OCR_NORMAL,
        CursorMethods.OCR_IBEAM,
        CursorMethods.OCR_WAIT,
        CursorMethods.OCR_CROSS,
        CursorMethods.OCR_UP,
        CursorMethods.OCR_SIZENWSE,
        CursorMethods.OCR_SIZENESW,
        CursorMethods.OCR_SIZEWE,
        CursorMethods.OCR_SIZENS,
        CursorMethods.OCR_SIZEALL,
        CursorMethods.OCR_NO,
        CursorMethods.OCR_HAND,
        CursorMethods.OCR_APPSTARTING,
    ];

    private static readonly object Gate = new();
    private static bool _hidden;
    private static Process? _watchdog;
    private static bool _hooksRegistered;

    private static string FlagPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MovieOverlay",
            "cursor-hidden.flag");

    public static void RegisterExitHooks()
    {
        lock (Gate)
        {
            if (_hooksRegistered)
            {
                return;
            }

            _hooksRegistered = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ForceRestoreSystemCursors();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => ForceRestoreSystemCursors();
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ForceRestoreSystemCursors();
                e.SetObserved();
            };
        }
    }

    /// <summary>Always restore default cursors (safe to call multiple times / from watchdog).</summary>
    public static void ForceRestoreSystemCursors()
    {
        lock (Gate)
        {
            try
            {
                CursorMethods.SystemParametersInfo(
                    CursorMethods.SPI_SETCURSORS,
                    0,
                    IntPtr.Zero,
                    CursorMethods.SPIF_SENDCHANGE);
            }
            catch
            {
                // best effort
            }

            ClearFlag();
            _hidden = false;
            StopWatchdog_NoLock();
        }
    }

    public static void RestoreIfFlagPresent()
    {
        if (File.Exists(FlagPath))
        {
            ForceRestoreSystemCursors();
        }
    }

    public static void Hide()
    {
        lock (Gate)
        {
            if (_hidden)
            {
                EnsureWatchdog_NoLock();
                return;
            }

            var blank = CreateBlankCursor();
            if (blank == IntPtr.Zero)
            {
                return;
            }

            try
            {
                foreach (var id in SystemCursorIds)
                {
                    var copy = CursorMethods.CopyIcon(blank);
                    if (copy == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!CursorMethods.SetSystemCursor(copy, id))
                    {
                        CursorMethods.DestroyCursor(copy);
                    }
                }

                WriteFlag();
                _hidden = true;
                EnsureWatchdog_NoLock();
            }
            finally
            {
                CursorMethods.DestroyCursor(blank);
            }
        }
    }

    public static void Show()
    {
        ForceRestoreSystemCursors();
    }

    public static bool TryHandleCli(string[] args)
    {
        if (args.Length >= 1 &&
            string.Equals(args[0], "--restore-cursors", StringComparison.OrdinalIgnoreCase))
        {
            ForceRestoreSystemCursors();
            return true;
        }

        if (args.Length >= 2 &&
            string.Equals(args[0], "--cursor-watchdog", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[1], out var parentPid))
        {
            RunWatchdog(parentPid);
            return true;
        }

        return false;
    }

    private static void RunWatchdog(int parentPid)
    {
        var handle = ProcessMethods.OpenProcess(ProcessMethods.SYNCHRONIZE, false, parentPid);
        if (handle == IntPtr.Zero)
        {
            // Parent already gone — restore immediately.
            ForceRestoreSystemCursors();
            return;
        }

        try
        {
            ProcessMethods.WaitForSingleObject(handle, ProcessMethods.INFINITE);
        }
        finally
        {
            ProcessMethods.CloseHandle(handle);
        }

        ForceRestoreSystemCursors();
    }

    private static void EnsureWatchdog_NoLock()
    {
        if (_watchdog is { HasExited: false })
        {
            return;
        }

        try
        {
            _watchdog?.Dispose();
        }
        catch
        {
            // ignored
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--cursor-watchdog {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        _watchdog = Process.Start(psi);
    }

    private static void StopWatchdog_NoLock()
    {
        try
        {
            if (_watchdog is { HasExited: false })
            {
                _watchdog.Kill(entireProcessTree: false);
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            _watchdog?.Dispose();
        }
        catch
        {
            // ignored
        }

        _watchdog = null;
    }

    private static void WriteFlag()
    {
        try
        {
            var dir = Path.GetDirectoryName(FlagPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // ignored
        }
    }

    private static void ClearFlag()
    {
        try
        {
            if (File.Exists(FlagPath))
            {
                File.Delete(FlagPath);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static IntPtr CreateBlankCursor()
    {
        const int width = 32;
        const int height = 32;
        var andPlane = new byte[width * height / 8];
        Array.Fill(andPlane, (byte)0xFF);
        var xorPlane = new byte[width * height / 8];

        return CursorMethods.CreateCursor(
            CursorMethods.GetModuleHandle(null),
            0,
            0,
            width,
            height,
            andPlane,
            xorPlane);
    }
}

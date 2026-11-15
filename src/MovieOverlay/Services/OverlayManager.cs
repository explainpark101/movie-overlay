using System.Runtime.InteropServices;
using MovieOverlay.Models;
using MovieOverlay.Native;

namespace MovieOverlay.Services;

/// <summary>
/// Pure Win32 black overlays. Two half-height windows per monitor avoid Focus Assist
/// treating a single fullscreen window as a full-screen app.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private const string WindowClassName = "MovieOverlayBlackout.v2";

    private static readonly object ClassLock = new();
    private static bool _classRegistered;
    private static NativeMethods.WndProc? _wndProcKeepAlive;
    private static IntPtr _blackBrush = IntPtr.Zero;

    private readonly Dictionary<string, OverlayPair> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private bool _visible;
    private bool _clickThrough = true;
    private bool _disposed;

    public bool IsVisible => _visible;
    public int OverlayWindowCount => _overlays.Values.Sum(p => p.WindowCount);

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value)
            {
                return;
            }

            _clickThrough = value;
            foreach (var pair in _overlays.Values)
            {
                pair.ApplyExStyle(BuildExStyle());
            }
        }
    }

    public void SyncTargets(IEnumerable<MonitorInfo> monitors)
    {
        EnsureClassRegistered();

        var wanted = monitors.ToDictionary(m => m.DeviceName, StringComparer.OrdinalIgnoreCase);

        foreach (var key in _overlays.Keys.ToList())
        {
            if (!wanted.ContainsKey(key))
            {
                _overlays[key].Dispose();
                _overlays.Remove(key);
            }
        }

        foreach (var (deviceName, monitor) in wanted)
        {
            if (_overlays.TryGetValue(deviceName, out var existing))
            {
                existing.Reposition(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
            }
            else
            {
                var pair = OverlayPair.Create(
                    monitor.Left,
                    monitor.Top,
                    monitor.Width,
                    monitor.Height,
                    BuildExStyle());
                _overlays[deviceName] = pair;
            }

            if (_visible)
            {
                _overlays[deviceName].Show();
            }
            else
            {
                _overlays[deviceName].Hide();
            }
        }
    }

    public void Show()
    {
        _visible = true;
        foreach (var pair in _overlays.Values)
        {
            pair.Show();
        }
    }

    public void Hide()
    {
        _visible = false;
        foreach (var pair in _overlays.Values)
        {
            pair.Hide();
        }
    }

    public void BringToFront()
    {
        if (!_visible)
        {
            return;
        }

        foreach (var pair in _overlays.Values)
        {
            pair.BringToFront();
        }
    }

    private uint BuildExStyle()
    {
        var style = NativeMethods.WS_EX_TOPMOST
                    | NativeMethods.WS_EX_TOOLWINDOW
                    | NativeMethods.WS_EX_NOACTIVATE
                    | NativeMethods.WS_EX_LAYERED;

        if (_clickThrough)
        {
            style |= NativeMethods.WS_EX_TRANSPARENT;
        }

        return style;
    }

    private static void EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return;
            }

            // COLORREF is 0x00BBGGRR — pure black.
            _blackBrush = NativeMethods.CreateSolidBrush(0x00000000);
            _wndProcKeepAlive = OverlayWndProc;
            var classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

            try
            {
                var wndClass = new NativeMethods.WNDCLASSEXW
                {
                    cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                    style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = NativeMethods.GetModuleHandleW(null),
                    hIcon = IntPtr.Zero,
                    hCursor = IntPtr.Zero,
                    hbrBackground = _blackBrush,
                    lpszMenuName = IntPtr.Zero,
                    lpszClassName = classNamePtr,
                    hIconSm = IntPtr.Zero,
                };

                var atom = NativeMethods.RegisterClassExW(ref wndClass);
                if (atom == 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 1410)
                    {
                        throw new InvalidOperationException($"RegisterClassExW failed: {error}");
                    }
                }

                _classRegistered = true;
            }
            finally
            {
                Marshal.FreeHGlobal(classNamePtr);
            }
        }
    }

    private static void PaintBlack(IntPtr hWnd, IntPtr hdc)
    {
        if (hdc == IntPtr.Zero || _blackBrush == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetClientRect(hWnd, out var rect);
        NativeMethods.FillRect(hdc, ref rect, _blackBrush);
    }

    private static IntPtr OverlayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_ERASEBKGND:
                PaintBlack(hWnd, wParam);
                return 1;
            case NativeMethods.WM_PAINT:
            {
                var hdc = NativeMethods.GetDC(hWnd);
                try
                {
                    PaintBlack(hWnd, hdc);
                }
                finally
                {
                    if (hdc != IntPtr.Zero)
                    {
                        NativeMethods.ReleaseDC(hWnd, hdc);
                    }
                }

                NativeMethods.ValidateRect(hWnd, IntPtr.Zero);
                return IntPtr.Zero;
            }
            case NativeMethods.WM_NCHITTEST:
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pair in _overlays.Values)
        {
            pair.Dispose();
        }

        _overlays.Clear();
    }

    private sealed class OverlayPair : IDisposable
    {
        private IntPtr _top;
        private IntPtr _bottom;
        private uint _exStyle;
        private int _left;
        private int _topY;
        private int _width;
        private int _height;
        private bool _disposed;

        public int WindowCount => (_top != IntPtr.Zero ? 1 : 0) + (_bottom != IntPtr.Zero ? 1 : 0);

        public static OverlayPair Create(int left, int top, int width, int height, uint exStyle)
        {
            var pair = new OverlayPair { _exStyle = exStyle };
            pair.CreateWindows(left, top, width, height);
            return pair;
        }

        private void CreateWindows(int left, int top, int width, int height)
        {
            _left = left;
            _topY = top;
            _width = Math.Max(1, width);
            _height = Math.Max(2, height);

            var half = _height / 2;
            var hInstance = NativeMethods.GetModuleHandleW(null);

            _top = CreateOne(left, top, _width, half, hInstance);
            _bottom = CreateOne(left, top + half, _width, _height - half, hInstance);
        }

        private IntPtr CreateOne(int x, int y, int w, int h, IntPtr hInstance)
        {
            var hwnd = NativeMethods.CreateWindowExW(
                _exStyle,
                WindowClassName,
                string.Empty,
                NativeMethods.WS_POPUP,
                x,
                y,
                w,
                h,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
            }

            // Opaque black layered window.
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
            return hwnd;
        }

        public void Reposition(int left, int top, int width, int height)
        {
            _left = left;
            _topY = top;
            _width = Math.Max(1, width);
            _height = Math.Max(2, height);
            var half = _height / 2;
            const uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER;

            if (_top != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_top, IntPtr.Zero, left, top, _width, half, flags);
                ForceRepaint(_top);
            }

            if (_bottom != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_bottom, IntPtr.Zero, left, top + half, _width, _height - half, flags);
                ForceRepaint(_bottom);
            }
        }

        public void ApplyExStyle(uint exStyle)
        {
            _exStyle = exStyle;
            ApplyExStyleTo(_top);
            ApplyExStyleTo(_bottom);
        }

        private void ApplyExStyleTo(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(nint)_exStyle);
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
            ForceRepaint(hwnd);
        }

        public void Show()
        {
            ShowOne(_top);
            ShowOne(_bottom);
        }

        private void ShowOne(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);

            var topMost = new IntPtr(NativeMethods.HWND_TOPMOST);
            NativeMethods.SetWindowPos(
                hwnd,
                topMost,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            ForceRepaint(hwnd);
        }

        public void Hide()
        {
            if (_top != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(_top, NativeMethods.SW_HIDE);
            }

            if (_bottom != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(_bottom, NativeMethods.SW_HIDE);
            }
        }

        public void BringToFront()
        {
            Show();
        }

        private static void ForceRepaint(IntPtr hwnd)
        {
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
            NativeMethods.RedrawWindow(
                hwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE | NativeMethods.RDW_UPDATENOW | NativeMethods.RDW_ALLCHILDREN);
            NativeMethods.UpdateWindow(hwnd);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_top != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_top);
                _top = IntPtr.Zero;
            }

            if (_bottom != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_bottom);
                _bottom = IntPtr.Zero;
            }
        }
    }
}

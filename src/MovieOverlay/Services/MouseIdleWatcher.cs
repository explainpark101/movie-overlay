using System.Windows.Threading;
using MovieOverlay.Native;

namespace MovieOverlay.Services;

public sealed class MouseIdleWatcher : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private NativeMethods.POINT _lastPoint;
    private DateTime _lastMoveUtc;
    private bool _hasPoint;
    private bool _isIdle;
    private bool _running;
    private bool _disposed;

    public event EventHandler? BecameIdle;
    public event EventHandler? BecameActive;
    public event EventHandler? MouseMoved;
    public event EventHandler? Tick;

    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromSeconds(5);
    public int MovementThresholdPixels { get; set; } = 5;

    public bool IsIdle => _isIdle;
    public bool IsRunning => _running;
    public DateTime LastMoveUtc => _lastMoveUtc;

    public double SecondsUntilIdle
    {
        get
        {
            if (!_running || _isIdle)
            {
                return 0;
            }

            var remaining = IdleThreshold.TotalSeconds - (DateTime.UtcNow - _lastMoveUtc).TotalSeconds;
            return Math.Max(0, remaining);
        }
    }

    public MouseIdleWatcher()
    {
        _lastMoveUtc = DateTime.UtcNow;
        _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        CapturePoint();
        _lastMoveUtc = DateTime.UtcNow;
        _isIdle = false;
        _running = true;
        _timer.Change(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        RaiseOnUi(Tick);
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_isIdle)
        {
            _isIdle = false;
            RaiseOnUi(BecameActive);
        }

        RaiseOnUi(Tick);
    }

    private void OnTimer(object? state)
    {
        if (!_running || _disposed)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        if (!_hasPoint)
        {
            _lastPoint = point;
            _hasPoint = true;
            _lastMoveUtc = DateTime.UtcNow;
            RaiseOnUi(Tick);
            return;
        }

        var dx = point.X - _lastPoint.X;
        var dy = point.Y - _lastPoint.Y;
        var threshold = Math.Max(0, MovementThresholdPixels);
        var moved = threshold <= 0
            ? dx != 0 || dy != 0
            : ((long)dx * dx) + ((long)dy * dy) >= (long)threshold * threshold;

        if (moved)
        {
            _lastPoint = point;
            _lastMoveUtc = DateTime.UtcNow;

            var wasIdle = _isIdle;
            _isIdle = false;

            RaiseOnUi(() =>
            {
                MouseMoved?.Invoke(this, EventArgs.Empty);
                if (wasIdle)
                {
                    BecameActive?.Invoke(this, EventArgs.Empty);
                }

                Tick?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        if (!_isIdle && DateTime.UtcNow - _lastMoveUtc >= IdleThreshold)
        {
            _isIdle = true;
            RaiseOnUi(() =>
            {
                BecameIdle?.Invoke(this, EventArgs.Empty);
                Tick?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        RaiseOnUi(Tick);
    }

    private void CapturePoint()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            _lastPoint = point;
            _hasPoint = true;
        }
    }

    private static void RaiseOnUi(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        RaiseOnUi(() => handler(null, EventArgs.Empty));
    }

    private static void RaiseOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action, DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;
        _timer.Dispose();
    }
}

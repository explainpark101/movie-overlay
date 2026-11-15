using System.Windows;
using MovieOverlay.Services;

namespace MovieOverlay;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Handle restore/watchdog before any WPF/UI or single-instance mutex.
        if (CursorHider.TryHandleCli(args))
        {
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}

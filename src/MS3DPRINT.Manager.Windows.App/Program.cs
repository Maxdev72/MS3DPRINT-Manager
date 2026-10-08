using Avalonia;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Windows.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}

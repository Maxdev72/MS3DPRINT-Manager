using Avalonia;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DesktopApplication.RemoteSessionStoreFactory = () => new LinuxRemoteSessionStore();
        DesktopPreviewLauncher.OpenWith = LinuxShell.OpenWith;
        DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}

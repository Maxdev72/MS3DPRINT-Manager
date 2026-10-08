using Avalonia;

namespace MS3DPRINT.Manager.Desktop;

public static class DesktopApplication
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

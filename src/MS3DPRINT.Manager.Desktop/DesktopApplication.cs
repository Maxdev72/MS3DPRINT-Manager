using Avalonia;

namespace MS3DPRINT.Manager.Desktop;

public static class DesktopApplication
{
    public static Func<MS3DPRINT.Manager.Core.Remote.IRemoteSessionStore>? RemoteSessionStoreFactory { get; set; }

    /// <summary>Operating-system notification (title, text), set by the host where it can show one.</summary>
    public static Action<string, string>? SystemNotification { get; set; }

    /// <summary>Set by the host when the operating system asks for fewer animations.</summary>
    public static bool ReduceMotion { get; set; }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

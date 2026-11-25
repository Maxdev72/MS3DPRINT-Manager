using Avalonia;

namespace MS3DPRINT.Manager.Desktop;

public static class DesktopApplication
{
    public static Func<MS3DPRINT.Manager.Core.Remote.IRemoteSessionStore>? RemoteSessionStoreFactory { get; set; }

    /// <summary>
    /// Operating-system notification (title, text, optional picture path), set by the host where it can show one.
    /// Clicking it brings the window back on the Surveillance page through <see cref="ShowPage"/>.
    /// </summary>
    public static Action<string, string, string?>? SystemNotification { get; set; }

    /// <summary>Brings the main window to the front on the named page; set by the main window, called by the host.</summary>
    public static Action<string>? ShowPage { get; set; }

    /// <summary>Set by the host when the operating system asks for fewer animations.</summary>
    public static bool ReduceMotion { get; set; }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

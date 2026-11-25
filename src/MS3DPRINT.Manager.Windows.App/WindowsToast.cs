using System.Security;
using Microsoft.Win32;
using MS3DPRINT.Manager.Desktop;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>
/// Windows notification (bottom right of the screen, kept in the notification centre), with a picture of the print
/// when there is one. An unpackaged application may show one once its identifier is declared under the user's
/// registry key. A click opens "ms3dprint://surveillance", a link declared for this executable: the running
/// application comes to the front on the Surveillance page (see <see cref="Program"/>).
/// </summary>
internal static class WindowsToast
{
    private const string AppId = "MS3DPRINT.Manager";
    public const string Protocol = "ms3dprint";
    public const string SurveillanceLink = Protocol + "://surveillance";
    private static bool _registered;

    public static void Show(string title, string text, string? picture)
    {
        try
        {
            if (!_registered) { Register(); _registered = true; }
            var image = picture is not null && File.Exists(picture)
                ? $"<image placement=\"hero\" src=\"{SecurityElement.Escape(new Uri(picture).AbsoluteUri)}\"/>" : "";
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast activationType="protocol" launch="{SurveillanceLink}">
                  <visual><binding template="ToastGeneric">{image}<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text></binding></visual>
                  <actions><action content="Voir les imprimantes" activationType="protocol" arguments="{SurveillanceLink}"/></actions>
                </toast>
                """);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        // A notification that cannot be shown must never disturb the application: the in-window toast remains.
        catch (Exception exception) { ErrorLog.Write(exception); }
    }

    /// <summary>Notification identity, and the "ms3dprint:" link that starts or wakes this executable.</summary>
    private static void Register()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
        {
            key.SetValue("DisplayName", "MS3DPRINT Manager");
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (File.Exists(icon)) key.SetValue("IconUri", icon);
        }
        if (Environment.ProcessPath is not { } exe) return;
        using var link = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Protocol);
        link.SetValue("", "URL:MS3DPRINT Manager");
        link.SetValue("URL Protocol", "");
        using var command = link.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exe}\" \"%1\"");
    }
}

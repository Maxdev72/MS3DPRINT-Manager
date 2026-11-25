using System.Security;
using Microsoft.Win32;
using MS3DPRINT.Manager.Desktop;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>
/// Windows notification (bottom right of the screen, kept in the notification centre). An unpackaged application
/// is allowed to show one once its identifier is declared under the user's registry key.
/// </summary>
internal static class WindowsToast
{
    private const string AppId = "MS3DPRINT.Manager";
    private static bool _registered;

    public static void Show(string title, string text)
    {
        try
        {
            if (!_registered)
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
                key.SetValue("DisplayName", "MS3DPRINT Manager");
                var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
                if (File.Exists(icon)) key.SetValue("IconUri", icon);
                _registered = true;
            }
            var xml = new XmlDocument();
            xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        // A notification that cannot be shown must never disturb the application: the in-window toast remains.
        catch (Exception exception) { ErrorLog.Write(exception); }
    }
}

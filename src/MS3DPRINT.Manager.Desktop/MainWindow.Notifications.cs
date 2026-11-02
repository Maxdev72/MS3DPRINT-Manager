using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly NotificationHistory _notifications = new();

    private void InitializeNotifications()
    {
        _notifications.Changed += UpdateNotificationCount;
        UpdateNotificationCount();
    }

    private NotificationEntry AddNotification(string title, string message, bool error = false, string? detail = null)
    {
        var entry = new NotificationEntry(title, message, error, detail);
        _notifications.Add(entry);
        return entry;
    }

    private void UpdateNotificationCount()
    {
        NotificationCount.Text = _notifications.Unread.ToString();
        NotificationCount.IsVisible = _notifications.Unread > 0;
        var name = $"Notifications · {_notifications.Unread} non lue(s)";
        AutomationProperties.SetName(NotificationCenterButton, name);
        ToolTip.SetTip(NotificationCenterButton, name);
    }

    private async void NotificationCenter_Click(object? sender, RoutedEventArgs e)
        => await ShowNotificationAsync();

    private async Task ShowNotificationAsync(NotificationEntry? selected = null)
    {
        if (!CanShowPanel) return;
        // An explicit request from a still-visible banner also recovers its result after history eviction/clearing.
        if (selected is not null && !_notifications.Entries.Contains(selected)) _notifications.Add(selected);
        await new NotificationCenterWindow(this, _notifications, selected).ShowDialog<bool>(this);
    }
}

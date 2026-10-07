using System.Windows;

namespace MS3DPRINT.Manager.App;

internal static class NavigationGuard
{
    internal static bool TryLeave(bool hasUnsavedChanges, Func<MessageBoxResult> confirm, Func<bool> save)
    {
        if (!hasUnsavedChanges) return true;
        return confirm() switch
        {
            MessageBoxResult.Yes => save(),
            MessageBoxResult.No => true,
            _ => false
        };
    }
}

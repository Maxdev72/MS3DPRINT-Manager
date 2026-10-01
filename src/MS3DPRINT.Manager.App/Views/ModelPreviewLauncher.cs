using System.Windows;

namespace MS3DPRINT.Manager.App.Views;

public static class ModelPreviewLauncher
{
    public static void Show(string? path, Window? owner)
    {
        try
        {
            var preview = new GpuModelPreviewWindow(path);
            if (owner is not null) preview.Owner = owner;
            preview.ShowDialog();
        }
        catch (Exception)
        {
            // A device/driver failure must not prevent access to the existing WPF viewer.
            var fallback = new ModelPreviewWindow(path);
            if (owner is not null) fallback.Owner = owner;
            fallback.ShowDialog();
        }
    }
}

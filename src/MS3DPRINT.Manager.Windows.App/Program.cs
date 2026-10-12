using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Windows.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DesktopPreviewLauncher.OpenDocument = path =>
        {
            var preview = new DocumentPreviewWindow(path);
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is not null) preview.Show(owner); else preview.Show();
        };
        DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}

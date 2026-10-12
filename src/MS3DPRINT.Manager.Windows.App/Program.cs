using Avalonia;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Windows.App;

internal static class Program
{
    /// <summary>
    /// The WPF preview windows read their styles and brushes from the WPF application resources.
    /// Under the Avalonia shell no WPF application exists, so one is created (without running it) on first use.
    /// </summary>
    private static void EnsureWpfResources()
    {
        if (System.Windows.Application.Current is not null) return;
        var wpf = new MS3DPRINT.Manager.App.App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        wpf.InitializeComponent();
        var dataDirectory = Environment.GetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY");
        MS3DPRINT.Manager.App.ThemeManager.Apply(new MS3DPRINT.Manager.Core.Storage.ThemeSettingsStore(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory).LoadAppearance());
    }

    [STAThread]
    public static void Main(string[] args)
    {
        DesktopPreviewLauncher.OpenThreeD = path => { EnsureWpfResources(); ModelPreviewLauncher.Show(path, null); };
        DesktopPreviewLauncher.OpenDocument = path => { EnsureWpfResources(); new DocumentPreviewWindow(path).ShowDialog(); };
        DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}

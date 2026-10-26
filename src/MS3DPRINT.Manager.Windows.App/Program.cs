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
        DesktopPreviewLauncher.OpenWith = path =>
        {
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            OpenWithDialog.Show(path, owner);
        };
        var ocr = new MS3DPRINT.Manager.Windows.WindowsOcrEngine();
        if (ocr.IsAvailable) MS3DPRINT.Manager.Core.Recognition.DocumentReader.Ocr = ocr;
        MS3DPRINT.Manager.Core.Recognition.DocumentReader.Rasterizer = new MS3DPRINT.Manager.Windows.WindowsPdfRasterizer();
        DesktopApplication.ReduceMotion = !ClientAreaAnimationsEnabled();
        DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Windows "Show animations" setting (Settings > Accessibility > Visual effects).</summary>
    private static bool ClientAreaAnimationsEnabled()
    {
        const uint SpiGetClientAreaAnimation = 0x1042;
        return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) || enabled;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool value, uint winIni);
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Windows.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Installer/updater hooks: must run first (handles --veloapp-* arguments, may exit).
        Velopack.VelopackApp.Build().Run();
        // Before any settings store is created: bring the preferences once kept next to the executable into the profile.
        try { MS3DPRINT.Manager.Core.Storage.LegacySettingsMigration.Run(MS3DPRINT.Manager.Core.Storage.ApplicationPaths.LegacyDataDirectory, MS3DPRINT.Manager.Core.Storage.ApplicationPaths.DataDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ErrorLog.Write(exception); }
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
        DesktopApplication.RemoteSessionStoreFactory = () => new MS3DPRINT.Manager.Windows.Remote.WindowsRemoteSessionStore();
        DesktopApplication.ReduceMotion = !ClientAreaAnimationsEnabled();
        _ = Task.Run(UpdateInBackgroundAsync);
        DesktopApplication.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Silent update: download in the background, install when the user closes the app. Does nothing when not installed (portable build) or offline.</summary>
    private static async Task UpdateInBackgroundAsync()
    {
        try
        {
            var manager = new Velopack.UpdateManager(new Velopack.Sources.GithubSource("https://github.com/Maxdev72/MS3DPRINT-Manager", null, false));
            if (!manager.IsInstalled) return;
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;
            await manager.DownloadUpdatesAsync(update);
            manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
            var message = $"Mise à jour {update.TargetFullRelease.Version} prête : elle s'installera à la fermeture du logiciel.";
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow window) window.NotifyInfo(message);
            });
        }
        catch (Exception exception) { ErrorLog.Write(exception); }
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

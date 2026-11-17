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
        // One Manager per user session: a second one could not hold the remote cache and would run without its server connection.
        using var single = new Mutex(false, @"Local\MS3DPRINT-Manager-single-instance");
        bool owned;
        try { owned = single.WaitOne(1500); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) { FocusRunningInstance(); return; }
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

    private static void FocusRunningInstance()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>Update on request: when a version is available, ask the user; on yes download, install and restart. Does nothing when not installed (portable build) or offline.</summary>
    private static async Task UpdateInBackgroundAsync()
    {
        try
        {
            var manager = new Velopack.UpdateManager(new Velopack.Sources.GithubSource("https://github.com/Maxdev72/MS3DPRINT-Manager", null, false));
            if (!manager.IsInstalled) return;
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;
            // The main window appears a moment after startup; wait for it (at most a minute) to ask the question.
            MainWindow? window = null;
            for (var attempt = 0; attempt < 60 && window is null; attempt++)
            {
                window = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow);
                if (window is null) await Task.Delay(1000);
            }
            if (window is null) return;
            var version = update.TargetFullRelease.Version.ToString();
            var accepted = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => new ConfirmUpdateWindow(version).ShowDialog<bool>(window));
            if (!accepted) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => window.NotifyInfo("Téléchargement de la mise à jour…"));
            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update);
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

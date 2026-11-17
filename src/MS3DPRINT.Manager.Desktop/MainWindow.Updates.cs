using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Hooks set by the Windows installer build; both stay null for a portable or development build.</summary>
public static class UpdateService
{
    /// <summary>Looks for a newer version: its number, or null when this one is the latest. Throws when the check fails.</summary>
    public static Func<Task<string?>>? Check { get; set; }

    /// <summary>Downloads the version found by the last check, installs it and restarts the application.</summary>
    public static Func<Task>? InstallAndRestart { get; set; }
}

/// <summary>Update checks: once shortly after startup, every 6 hours, and on demand from the settings. Installing always asks first.</summary>
public partial class MainWindow
{
    private string? _declinedUpdate;
    private bool _updateBusy;

    private void StartUpdateChecks()
    {
        if (UpdateService.Check is null) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        timer.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
        timer.Start();
        DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(10));
    }

    private bool UpdateCheckMayInterrupt => _panel is null && _busy == 0;

    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

    /// <summary>A periodic check stays silent unless a new version is found, and is skipped while the user is busy (the next tick retries).</summary>
    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy) return;
        if (UpdateService.Check is not { } check)
        {
            if (manual) Notify("Les mises à jour ne sont disponibles qu’avec la version installée par Setup.exe.");
            return;
        }
        if (!manual && !UpdateCheckMayInterrupt) return;
        _updateBusy = true;
        try
        {
            string? version;
            try { version = await check(); }
            catch (Exception exception)
            {
                ErrorLog.Write(exception);
                if (manual) Notify("Impossible de vérifier les mises à jour. Vérifiez votre connexion.", error: true);
                return;
            }
            if (version is null) { if (manual) Notify("Vous utilisez la dernière version."); return; }
            // A version declined once is not offered again by the timer; the manual button always asks.
            if (!manual && (version == _declinedUpdate || !UpdateCheckMayInterrupt)) return;
            if (!await new ConfirmUpdateWindow(version).ShowDialog<bool>(this)) { _declinedUpdate = version; return; }
            Notify("Téléchargement de la mise à jour…");
            try { await UpdateService.InstallAndRestart!(); }
            catch (Exception exception)
            {
                ErrorLog.Write(exception);
                Notify("La mise à jour n’a pas pu être installée. Réessayez plus tard.", error: true);
            }
        }
        finally { _updateBusy = false; }
    }
}

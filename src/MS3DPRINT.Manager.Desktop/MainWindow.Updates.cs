using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Hooks set by the Windows installer build; both stay null for a portable or development build.</summary>
public static class UpdateService
{
    /// <summary>Looks for a newer version: its number, or null when this one is the latest. Throws when the check fails.</summary>
    public static Func<Task<string?>>? Check { get; set; }

    /// <summary>Downloads the version found by the last check.</summary>
    public static Func<Task>? Download { get; set; }

    /// <summary>Starts the installer, which waits for the application to close normally, then installs and restarts it.</summary>
    public static Action? InstallAfterExit { get; set; }
}

/// <summary>
/// Update checks: shortly after startup, every 15 minutes, when the window comes back to the front (at most every
/// 10 minutes) and on demand. A new version is downloaded in the background first, so installing it only takes a
/// restart; installing always asks, and a version put off stays one click away in the side bar.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ActivationCheckInterval = TimeSpan.FromMinutes(10);
    private string? _declinedUpdate;
    private string? _readyUpdate;
    private bool _updateBusy;
    private DateTime _lastUpdateCheck;

    private void StartUpdateChecks()
    {
        if (UpdateService.Check is null) return;
        var timer = new DispatcherTimer { Interval = UpdateInterval };
        timer.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
        timer.Start();
        DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(10));
        Activated += async (_, _) => { if (DateTime.UtcNow - _lastUpdateCheck > ActivationCheckInterval) await CheckForUpdateAsync(manual: false); };
    }

    private bool UpdateCheckMayInterrupt => _panel is null && _busy == 0;

    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

    /// <summary>A periodic check stays silent unless a new version is found, and only asks while the user is not busy (the next tick retries).</summary>
    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy) return;
        if (UpdateService.Check is not { } check)
        {
            if (manual) Notify("Les mises à jour ne sont disponibles qu’avec la version installée par Setup.exe.");
            return;
        }
        _updateBusy = true;
        _lastUpdateCheck = DateTime.UtcNow;
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
            if (_readyUpdate != version)
            {
                if (manual) Notify("Téléchargement de la mise à jour…");
                try { await UpdateService.Download!(); }
                catch (Exception exception)
                {
                    ErrorLog.Write(exception);
                    if (manual) Notify("La mise à jour n’a pas pu être téléchargée. Réessayez plus tard.", error: true);
                    return;
                }
                _readyUpdate = version;
                UpdateReadyText.Text = $"Installer la version {version}";
                UpdateReadyButton.IsVisible = true;
            }
            // A version put off is not asked again by the timer: the side bar button stays until the user installs it.
            if (!manual && (version == _declinedUpdate || !UpdateCheckMayInterrupt)) return;
            if (!await new ConfirmUpdateWindow(version).ShowDialog<bool>(this)) { _declinedUpdate = version; return; }
            // Never cut an import or a save short: the download stays ready for a new try.
            if (OperationInProgress())
            {
                Notify("Mise à jour prête. Terminez l’opération en cours, puis cliquez sur « Installer la version " + version + " ».");
                return;
            }
            UpdateService.InstallAfterExit!();
            // The normal close saves the estimate draft and the window position before the installer takes over.
            QuitApplication();
        }
        finally { _updateBusy = false; }
    }
}

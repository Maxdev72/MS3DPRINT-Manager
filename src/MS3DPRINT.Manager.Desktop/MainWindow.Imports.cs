using MS3DPRINT.Manager.Core.Workspace;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _importBatchActive;
    private bool _importStopRequested;
    private readonly List<string> _importErrors = [];

    private void StartImportBatch(int total)
    {
        _importBatchActive = true; _importStopRequested = false; _importErrors.Clear();
        ImportBatchProgress.Maximum = total; ImportBatchProgress.Value = 0;
        ImportBatchHeading.Text = $"Import de {total} fichier{(total > 1 ? "s" : "")}";
        ImportBatchFile.Text = "Préparation de l’import…";
        ImportBatchSummary.Text = "Vous pouvez arrêter la suite sans annuler les copies terminées.";
        ImportBatchErrors.Text = ""; ImportBatchDetails.IsVisible = false; ImportBatchDetails.IsExpanded = false;
        ImportBatchStop.IsVisible = true; ImportBatchStop.IsEnabled = true; ImportBatchStop.Content = "Arrêter après ce fichier";
        ImportBatchDismiss.IsVisible = false; ImportBatchPanel.IsVisible = true;
    }

    private void UpdateImportProgress(int number, int total, string source, string stage)
    {
        ImportBatchHeading.Text = $"Fichier {number} sur {total} · {stage}";
        ImportBatchFile.Text = Path.GetFileName(source); ToolTip.SetTip(ImportBatchFile, source);
    }

    private void StopImportBatch_Click(object? sender, RoutedEventArgs e)
    {
        if (!_importBatchActive) return;
        _importStopRequested = true; ImportBatchStop.IsEnabled = false;
        ImportBatchStop.Content = "Arrêt demandé";
        ImportBatchSummary.Text = "La copie en cours est conservée. Aucun fichier suivant ne sera importé.";
    }

    private void DismissImportBatch_Click(object? sender, RoutedEventArgs e)
    {
        if (!_importBatchActive) ImportBatchPanel.IsVisible = false;
    }

    private void CompleteImportBatch(int imported, int failures, int selected, bool stopped)
    {
        _importBatchActive = false;
        var remaining = Math.Max(0, selected - imported - failures);
        ImportBatchProgress.Value = imported + failures;
        ImportBatchHeading.Text = stopped ? "Import arrêté" : "Import terminé";
        ImportBatchFile.Text = "Les copies terminées sont conservées."; ToolTip.SetTip(ImportBatchFile, null);
        ImportBatchSummary.Text = $"{imported} importé{(imported > 1 ? "s" : "")} · {failures} échec{(failures > 1 ? "s" : "")} · {remaining} non traité{(remaining > 1 ? "s" : "")}.";
        ImportBatchErrors.Text = string.Join(Environment.NewLine + Environment.NewLine, _importErrors);
        ImportBatchDetails.IsVisible = _importErrors.Count > 0;
        ImportBatchStop.IsVisible = false; ImportBatchDismiss.IsVisible = true;
    }

    private void RecordImportError(string message)
    {
        _importErrors.Add(message);
        Notify(message, error: true);
    }

    private async Task<string[]> PickImportSourcesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer des fichiers", AllowMultiple = true });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task ImportWorkspaceFilesAsync(string root, string directory, IReadOnlyList<string> sources)
    {
        if (_importBatchActive || sources.Count == 0) return;
        StartImportBatch(sources.Count);
        var navigationKey = CurrentNavigationStop?.Key;
        var importedCount = 0; var failures = 0; var stopped = false;
        try
        {
            for (var index = 0; index < sources.Count; index++)
            {
                if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                var source = sources[index];
                UpdateImportProgress(index + 1, sources.Count, source, "Copie en cours");
                try
                {
                    var imported = await Task.Run(() => new ManagedFileService(root).Import(source, directory));
                    importedCount++;
                    ImportBatchProgress.Value = importedCount + failures;
                    if (!MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                    UpdateImportProgress(index + 1, sources.Count, source, "Copié · choix pour la source");
                    await ConfirmImportedSourceRemovalAsync(root, source, imported);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { failures++; ImportBatchProgress.Value = importedCount + failures; RecordImportError(Path.GetFileName(source) + " : " + exception.Message); }
            }
        }
        finally { stopped |= _importStopRequested; CompleteImportBatch(importedCount, failures, sources.Count, stopped); }
        if (MatchesNavigationContext(root, navigationKey)) await LoadFolderWorkspaceAsync();
    }

    private async Task ConfirmImportedSourceRemovalAsync(string root, string source, string imported)
    {
        if (!await new ConfirmImportSourceWindow(source, imported, _importBatchActive ? () => StopImportBatch_Click(null, new RoutedEventArgs()) : null).ShowDialog<bool>(this)) return;
        BeginBusy();
        try
        {
            await Task.Run(() => new ManagedFileService(root).DeleteImportedSource(source, imported));
            Notify("Fichier importé ; original supprimé du dossier source.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            RecordImportError(Path.GetFileName(source) + " : la copie importée est conservée. Impossible de supprimer la source : " + exception.Message);
        }
        finally { EndBusy(); }
    }
}

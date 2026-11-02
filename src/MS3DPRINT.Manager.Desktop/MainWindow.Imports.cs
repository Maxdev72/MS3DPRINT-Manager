using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Naming;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _importBatchActive;
    private bool _importStopRequested;
    private readonly List<string> _importErrors = [];
    private NotificationEntry? _importResultNotification;

    private void StartImportBatch(int total)
    {
        _importBatchActive = true; _importStopRequested = false; _importErrors.Clear();
        _importSourcesDeleted = 0; _importSourceFailures = 0; _importDestination = null; _importCopiedFiles.Clear(); _importMultiple = total > 1;
        _importResultNotification = null;
        ImportBatchProgress.IsVisible = true; ImportBatchFile.IsVisible = true;
        ImportBatchProgress.Maximum = total; ImportBatchProgress.Value = 0;
        SetImportBannerIcon("IconUpload", "CyanBrush");
        ImportBatchHeading.Text = $"Import de {total} fichier{(total > 1 ? "s" : "")}";
        ImportBatchFile.Text = "Préparation de l’import…";
        ImportBatchSummary.Text = "Vous pouvez arrêter la suite sans annuler les copies terminées.";
        ToolTip.SetTip(ImportBatchSummary, null);
        ImportBatchView.IsVisible = false;
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

    private async void ViewImportBatch_Click(object? sender, RoutedEventArgs e)
        => await ShowNotificationAsync(_importResultNotification);

    private void SetImportBannerIcon(string icon, string brush)
    {
        if (Avalonia.Application.Current?.TryFindResource(icon, out var geometry) == true && geometry is Avalonia.Media.Geometry data) ImportBatchStatusIcon.Data = data;
        if (Avalonia.Application.Current?.TryFindResource(brush, out var resource) == true && resource is Avalonia.Media.IBrush color) ImportBatchStatusIcon.Stroke = color;
    }

    private void CompleteImportBatch(int imported, int failures, int selected, bool stopped)
    {
        _importBatchActive = false;
        var remaining = Math.Max(0, selected - imported - failures);
        ImportBatchProgress.Value = imported + failures;
        ImportBatchProgress.IsVisible = false; ImportBatchFile.IsVisible = false;
        SetImportBannerIcon(_importErrors.Count > 0 || stopped ? "IconAlert" : "IconCheck", _importErrors.Count > 0 || stopped ? "WarningBrush" : "SuccessBrush");
        ImportBatchHeading.Text = stopped ? "Import arrêté" : "Import terminé";
        ImportBatchFile.Text = "Les copies terminées sont conservées."; ToolTip.SetTip(ImportBatchFile, null);
        ImportBatchSummary.Text = $"{imported} importé{(imported > 1 ? "s" : "")} · {failures} échec{(failures > 1 ? "s" : "")} · {remaining} non traité{(remaining > 1 ? "s" : "")}.";
        var fullSummary = ImportBatchSummary.Text;
        if (selected > 1) fullSummary += $" {_importSourcesDeleted} source(s) supprimée(s) · {Math.Max(0, imported - _importSourcesDeleted)} conservée(s).";
        if (_importSourceFailures > 0)
        {
            var warning = $" {_importSourceFailures} suppression(s) impossible(s).";
            ImportBatchSummary.Text += warning; fullSummary += warning;
        }
        ToolTip.SetTip(ImportBatchSummary, fullSummary);
        ImportBatchView.Content = _importErrors.Count > 0 ? "Voir les erreurs" : "Voir le bilan";
        ImportBatchView.IsVisible = true;
        ImportBatchStop.IsVisible = false; ImportBatchDismiss.IsVisible = true;
        _importResultNotification = AddNotification(stopped ? "Import arrêté" : "Import terminé", fullSummary!,
            _importErrors.Count > 0, string.Join(Environment.NewLine + Environment.NewLine, new[] { _importDestination }.Concat(_importCopiedFiles).Concat(_importErrors).Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private void RecordImportError(string message)
    {
        _importErrors.Add(message);
        if (!_importBatchActive || !_importMultiple) Notify(message, error: true);
    }

    private async Task<string[]> PickImportSourcesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer des fichiers", AllowMultiple = true });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task ImportWorkspaceFilesAsync(string root, string directory, IReadOnlyList<string> sources)
    {
        if (_importBatchActive || sources.Count == 0) return;
        if (sources.Count > 1) { await ImportMultipleFilesAsync(root, directory, sources); return; }
        StartImportBatch(sources.Count);
        _importDestination = directory;
        var navigationKey = CurrentNavigationStop?.Key;
        var importedCount = 0; var failures = 0; var stopped = false;
        try
        {
            var category = Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var collectionReference = category is "02_MODELES_3D" or "03_PRODUITS_MS3DPRINT" or "06_FOURNISSEURS"
                ? await Task.Run(() => CollectionImportReference(root, directory, category)) : null;
            for (var index = 0; index < sources.Count; index++)
            {
                if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                var source = sources[index];
                UpdateImportProgress(index + 1, sources.Count, source, "Copie en cours");
                try
                {
                    string? finalName = null;
                    if (collectionReference is not null)
                    {
                        UpdateImportProgress(index + 1, sources.Count, source, "Choix du nom");
                        var originalName = Path.GetFileName(source);
                        var naming = new ImportNameWindow(originalName, ProjectFileClassifier.SuggestImportName(originalName, collectionReference),
                            collectionReference, fileNumber: index + 1, totalFiles: sources.Count, isCollection: true);
                        if (!await naming.ShowDialog<bool>(this)) { stopped = true; break; }
                        if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                        finalName = naming.FileName;
                    }
                    UpdateImportProgress(index + 1, sources.Count, source, "Copie en cours");
                    var imported = await Task.Run(() => new ManagedFileService(root).Import(source, directory, finalName));
                    _importCopiedFiles.Add(source + " → " + imported);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { stopped = true; RecordImportError(exception.Message); }
        finally { stopped |= _importStopRequested; CompleteImportBatch(importedCount, failures, sources.Count, stopped); }
        if (MatchesNavigationContext(root, navigationKey)) await LoadFolderWorkspaceAsync();
    }

    private static string? CollectionImportReference(string root, string directory, string category)
    {
        var collection = new CollectionCatalog().Load(root, category)
            .Where(item => WorkspaceEntityPaths.IsInside(item.Path, directory))
            .OrderByDescending(item => item.Path.Length).FirstOrDefault();
        if (collection is null) return null;
        var reference = NameNormalizer.Normalize(collection.Name);
        return reference.Length == 0 ? null : reference;
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

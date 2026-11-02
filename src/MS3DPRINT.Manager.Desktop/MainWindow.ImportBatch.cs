using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private int _importSourcesDeleted;
    private int _importSourceFailures;
    private string? _importDestination;
    private readonly List<string> _importCopiedFiles = [];
    private bool _importMultiple;

    private async Task ImportMultipleFilesAsync(string root, string directory, IReadOnlyList<string> sources, ProjectSummary? project = null)
    {
        StartImportBatch(sources.Count);
        _importDestination = directory;
        var navigationKey = CurrentNavigationStop?.Key;
        var imported = 0; var failures = 0; var stopped = false;
        var copies = new List<ImportedSource>();
        try
        {
            // Read proposals off the UI thread; the form itself is built and read on the UI thread.
            var preparation = await Task.Run(() =>
            {
                var category = Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                var reference = project?.Reference ?? (category is "02_MODELES_3D" or "03_PRODUITS_MS3DPRINT" or "06_FOURNISSEURS"
                    ? CollectionImportReference(root, directory, category) : null);
                var atRoot = project is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.ProjectPath)), StringComparison.OrdinalIgnoreCase);
                var destinations = atRoot ? ImportDestinations(project!.ProjectPath) : null;
                var template = atRoot ? new ProjectTemplateStore(new WorkspaceMetadataPaths(root)).Load() : null;
                var items = sources.Select(source =>
                {
                    var name = Path.GetFileName(source);
                    var target = directory;
                    if (atRoot)
                    {
                        try { target = Path.Combine(project!.ProjectPath, ProjectFileClassifier.Suggest(name, project.FolderName, template!).RelativeDirectory); }
                        catch (ArgumentException) { }
                    }
                    return new ImportBatchItem(source, reference is null ? name : ProjectFileClassifier.SuggestImportName(name, reference), target);
                }).ToArray();
                return (items, destinations, reference);
            });
            if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; return; }
            ImportBatchHeading.Text = $"Vérification des {sources.Count} fichiers";
            var naming = new ImportBatchWindow(preparation.items, preparation.destinations, preparation.reference);
            if (!await naming.ShowDialog<bool>(this) || _importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; return; }
            var items = naming.Items;
            for (var index = 0; index < items.Count; index++)
            {
                if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                var item = items[index];
                UpdateImportProgress(index + 1, items.Count, item.Source, "Copie en cours");
                try
                {
                    var target = await Task.Run(() => new ManagedFileService(root).Import(item.Source, item.Directory, item.FileName));
                    copies.Add(new ImportedSource(item.Source, target)); imported++;
                    _importCopiedFiles.Add(item.Source + " → " + target);
                }
                catch (Exception exception) when (ImportOperationError(exception))
                { failures++; RecordImportError(Path.GetFileName(item.Source) + " : " + exception.Message); }
                ImportBatchProgress.Value = imported + failures;
            }
            stopped |= _importStopRequested || !MatchesNavigationContext(root, navigationKey);
            if (copies.Count > 0 && !stopped)
            {
                ImportBatchHeading.Text = "Copies terminées · choix pour les sources";
                var confirmation = new ConfirmImportSourcesWindow(copies);
                if (await confirmation.ShowDialog<bool>(this))
                {
                    var selected = confirmation.Selected;
                    ImportBatchStop.IsVisible = false;
                    foreach (var copy in selected)
                    {
                        // Authorization applies to this immutable source/copy pair in the original workspace.
                        ImportBatchHeading.Text = "Vérification et suppression des sources";
                        ImportBatchFile.Text = Path.GetFileName(copy.Source);
                        try
                        {
                            await Task.Run(() => new ManagedFileService(root).DeleteImportedSource(copy.Source, copy.Imported));
                            _importSourcesDeleted++;
                        }
                        catch (Exception exception) when (ImportOperationError(exception))
                        { _importSourceFailures++; RecordImportError(Path.GetFileName(copy.Source) + " : copie conservée, impossible de supprimer la source : " + exception.Message); }
                    }
                }
            }
        }
        catch (Exception exception) when (ImportOperationError(exception))
        { stopped = true; RecordImportError(exception.Message); }
        finally { CompleteImportBatch(imported, failures, sources.Count, stopped || _importStopRequested); }
        if (MatchesNavigationContext(root, navigationKey))
        {
            if (project is null) await LoadFolderWorkspaceAsync();
            else await LoadProjectDirectoryAsync(project, directory);
        }
    }

    private static bool ImportOperationError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
}

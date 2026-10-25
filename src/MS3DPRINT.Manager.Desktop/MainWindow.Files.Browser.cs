using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private const string FileRowColumns = "*,90,90,64";
    private CancellationTokenSource? _fileReadCancellation;
    private FileBrowserView? _activeFileBrowser;
    private (string Root, string Directory)? _activeFileBrowserKey;
    private readonly Dictionary<(string Root, string Directory), FileBrowserState> _fileBrowserStates = new();

    private void CancelFileRead()
    {
        if (_activeFileBrowser is { } browser && _activeFileBrowserKey is { } key)
        {
            _fileBrowserStates[key] = browser.CaptureState();
            if (_fileBrowserStates.Count > 100) _fileBrowserStates.Remove(_fileBrowserStates.Keys.First());
        }
        _activeFileBrowser = null; _activeFileBrowserKey = null;
        _fileReadCancellation?.Cancel(); _fileReadCancellation = null;
    }

    private async Task LoadFileBrowserAsync(StackPanel host, string browseRoot, string directory, ProjectSummary? project, Func<Task> reload)
    {
        // The caller cancels before clearing its host so scroll state is still available.
        var root = WorkspaceRoot;
        var key = (root.ToUpperInvariant(), Path.GetFullPath(directory).ToUpperInvariant());
        foreach (var oldKey in _fileBrowserStates.Keys.Where(k => k.Root != key.Item1).ToArray()) _fileBrowserStates.Remove(oldKey);
        using var cancellation = new CancellationTokenSource();
        _fileReadCancellation = cancellation;
        var browser = BuildFileBrowser([], directory, project, reload, _fileBrowserStates.GetValueOrDefault(key), loading: true);
        _activeFileBrowser = browser; _activeFileBrowserKey = key;
        host.Children.Add(browser);
        bool Current() => !cancellation.IsCancellationRequested && ReferenceEquals(_fileReadCancellation, cancellation)
            && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
        try
        {
            await Task.Run(async () =>
            {
                var batch = new List<ProjectFileEntry>(500);
                var first = true;
                var sinceFlush = System.Diagnostics.Stopwatch.StartNew();
                foreach (var entry in new ProjectFileBrowser().Enumerate(browseRoot, directory, cancellation.Token))
                {
                    batch.Add(entry);
                    if (batch.Count < (first ? 50 : 500) && sinceFlush.ElapsedMilliseconds < 100) continue;
                    var ready = batch.ToArray(); batch.Clear(); first = false;
                    await Dispatcher.UIThread.InvokeAsync(() => { if (Current()) browser.AddEntries(ready, complete: false); });
                    sinceFlush.Restart();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                var last = batch.ToArray();
                await Dispatcher.UIThread.InvokeAsync(() => { if (Current()) browser.AddEntries(last, complete: true); });
            }, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (Current()) browser.ShowReadError(CreateReadErrorState("Dossier indisponible",
                "Vérifiez que le dossier est accessible et que la synchronisation est terminée, puis réessayez.", exception, reload));
        }
        finally { if (ReferenceEquals(_fileReadCancellation, cancellation)) _fileReadCancellation = null; }
    }

    private FileBrowserView BuildFileBrowser(IReadOnlyList<ProjectFileEntry> entries, string directory, ProjectSummary? project, Func<Task> reload,
        FileBrowserState? state = null, bool loading = false)
    {
        var browser = new FileBrowserView(entries, entry =>
        {
            var open = new Button { Tag = entry.FullPath, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            if (entry.IsDirectory)
                open.Click += async (_, _) =>
                {
                    if (project is not null) await LoadProjectDirectoryAsync(project, entry.FullPath);
                    else { _folderWorkspaceDirectory = entry.FullPath; await LoadFolderWorkspaceAsync(); }
                };
            else
            {
                open.Click += FileEntry_Click;
                if (ThreeDFileSupport.IsPreviewable(entry.FullPath)) AttachLargePreview(open, entry.FullPath, entry.Name);
            }
            return CreateActionRow(FileRowColumns, open, CreateOverflowActions(EntryActions(new(project, directory, entry.FullPath), !entry.IsDirectory)),
                CreateFileNameCell(entry), CreateCompactTableCell(entry.TypeLabel), CreateCompactTableCell(entry.IsDirectory ? "—" : entry.SizeLabel));
        }, () => CreateCompactTableHeader(FileRowColumns, "NOM", "FORMAT", "TAILLE", "ACTIONS"), reload, state, loading);
        var root = WorkspaceRoot;
        browser.MoveSelectionRequested = paths => RunFileBatchAsync(browser, root, directory, paths, FileBatchOperation.Move, reload);
        browser.TrashSelectionRequested = paths => RunFileBatchAsync(browser, root, directory, paths, FileBatchOperation.Trash, reload);
        bool Available() => ReferenceEquals(_activeFileBrowser, browser) && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
                && _panel is null && !_importBatchActive && !_pendingOperations.Contains(root + "|files-batch");
        browser.FileCommandsAvailable = Available;
        browser.RenameSelectionRequested = path => RenameProjectEntryAsync(new(project, directory, path));
        browser.ConfigureFileDrop(directory, Available,
            async paths =>
            {
                if (project is not null) await ImportProjectFilesAsync(root, project, directory, paths);
                else await ImportWorkspaceFilesAsync(root, directory, paths);
            });
        return browser;
    }

    private static Control CreateFileNameCell(ProjectFileEntry entry)
    {
        var grid = (Grid)CreateNameCell(entry.IsDirectory ? "IconProjects" : "IconFile", entry.Name, entry.IsDirectory, entry.IsDirectory ? null : entry.FullPath);
        if (grid.Children.LastOrDefault() is TextBlock name)
        {
            grid.Children.Remove(name);
            var hint = entry.IsDirectory ? "Ouvrir le dossier" : ThreeDFileSupport.IsPreviewable(entry.FullPath) ? "Aperçu 3D" :
                PreviewFileSupport.GetKind(entry.FullPath) != PreviewFileKind.None ? "Afficher le document" : "Ouvrir dans l’application associée";
            var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children =
            { name, new TextBlock { Text = hint, FontSize = 12, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis } } };
            Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        }
        return grid;
    }
}

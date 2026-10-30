using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private sealed record NavigationStop(string Key, Func<Task> Restore, FolderNavigationLocation? Folder = null, ViewerNavigationLocation? Viewer = null);
    private sealed record FolderNavigationLocation(string Root, string Directory, NavigationStop? Parent, string? Title, string? Subtitle);
    private sealed record ViewerNavigationLocation(string? Path, NavigationStop? Parent);
    private readonly List<NavigationStop> _navigationHistory = [];
    private int _navigationPosition = -1;
    private bool _replayingNavigation;
    private bool _historyNavigationBusy;
    private bool _showingDetail;
    private string? _navigationWorkspaceRoot;
    private NavigationStop? _folderHistoryParent;
    private NavigationStop? _viewerHistoryParent;
    private NavigationStop? CurrentNavigationStop => _navigationPosition >= 0 ? _navigationHistory[_navigationPosition] : null;
    private bool MatchesNavigationContext(string root, string? key)
        => string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase) && CurrentNavigationStop?.Key == key;

    private void InitializeNavigationHistory()
    {
        RememberWorkspace("Accueil");
        AddHandler(PointerPressedEvent, HistoryPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, HistoryKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void EnsureNavigationWorkspace()
    {
        var root = WorkspaceRoot;
        if (string.Equals(root, _navigationWorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        _navigationWorkspaceRoot = root;
        _navigationHistory.Clear();
        _navigationPosition = -1;
        _folderHistoryParent = _viewerHistoryParent = null;
    }

    private void RememberNavigation(NavigationStop stop)
    {
        EnsureNavigationWorkspace();
        if (CurrentNavigationStop?.Key != stop.Key) CancelFileRead();
        if (_replayingNavigation) return;
        if (CurrentNavigationStop?.Key == stop.Key) { _navigationHistory[_navigationPosition] = stop; return; }
        if (_navigationPosition + 1 < _navigationHistory.Count)
            _navigationHistory.RemoveRange(_navigationPosition + 1, _navigationHistory.Count - _navigationPosition - 1);
        _navigationHistory.Add(stop);
        if (_navigationHistory.Count > 100) _navigationHistory.RemoveAt(0);
        _navigationPosition = _navigationHistory.Count - 1;
    }

    private void RememberWorkspace(string destination) => RememberNavigation(new("page:" + destination, () => NavigateToWorkspaceAsync(destination)));

    // Direct links to a sheet must not insert an unvisited list page into the history.
    private void ShowWorkspaceForDetail(string destination)
    {
        _showingDetail = true;
        try { ShowWorkspace(destination); }
        finally { _showingDetail = false; }
    }

    private void RememberProject(ProjectSummary project, string directory)
    {
        var origin = _projectOriginClient;
        RememberNavigation(new("project:" + directory, async () =>
        {
            var root = WorkspaceRoot;
            var current = (await Task.Run(() => new WorkspaceProjectReader().Load(root))).FirstOrDefault(p =>
                project.Profile is { } profile ? p.Profile?.Id == profile.Id : p.Reference == project.Reference);
            if (current is null) throw new InvalidOperationException("Ce projet n’est plus disponible dans cet espace de travail.");
            var relative = Path.GetRelativePath(project.ProjectPath, directory);
            var target = Path.GetFullPath(Path.Combine(current.ProjectPath, relative));
            if (!Directory.Exists(target)) target = current.ProjectPath;
            ShowWorkspaceForDetail("Projets");
            _projectOriginClient = origin is null ? null : (await Task.Run(() => new WorkspaceClientReader().Load(root))).FirstOrDefault(c =>
                origin.Profile is { } clientProfile ? c.Profile?.Id == clientProfile.Id : c.ClientCode == origin.ClientCode);
            PageTitle.Text = current.Title();
            PageSubtitle.Text = $"{current.Reference} · {current.ClientTitle()}";
            await LoadProjectDirectoryAsync(current, target);
        }));
    }

    private void RememberClient(ClientSummary client) => RememberNavigation(new("client:" + client.ClientCode, async () =>
    {
        var root = WorkspaceRoot;
        var current = (await Task.Run(() => new WorkspaceClientReader().Load(root))).FirstOrDefault(c =>
            client.Profile is { } profile ? c.Profile?.Id == profile.Id : c.ClientCode == client.ClientCode);
        if (current is null) throw new InvalidOperationException("Ce client n’est plus disponible dans cet espace de travail.");
        ShowWorkspaceForDetail("Clients");
        _projectOriginClient = null;
        PageTitle.Text = "Clients";
        PageSubtitle.Text = current.Title();
        await LoadClientDetailAsync(current);
    }));

    private void RememberFolder()
    {
        var root = _folderWorkspaceRoot;
        var directory = _folderWorkspaceDirectory;
        var parent = _folderHistoryParent;
        var title = _folderWorkspaceReturnTitle;
        var subtitle = _folderWorkspaceReturnSubtitle;
        if (root is null || directory is null) return;
        RememberNavigation(CreateFolderNavigationStop(new(root, directory, parent, title, subtitle)));
    }

    private NavigationStop CreateFolderNavigationStop(FolderNavigationLocation location)
        => new("folder:" + location.Directory, async () =>
        {
            if (!Directory.Exists(location.Root) || !Directory.Exists(location.Directory)) throw new InvalidOperationException("Ce dossier n’est plus disponible.");
            if (location.Parent is not null) await location.Parent.Restore();
            _folderHistoryParent = location.Parent;
            _folderWorkspaceRoot = location.Root; _folderWorkspaceDirectory = location.Directory;
            _folderWorkspaceReturnTitle = location.Title; _folderWorkspaceReturnSubtitle = location.Subtitle;
            _hiddenByFolderWorkspace = WorkspacePanels().Where(p => p.IsVisible).ToList();
            foreach (var panel in _hiddenByFolderWorkspace) panel.IsVisible = false;
            FolderWorkspacePanel.IsVisible = true;
            await LoadFolderWorkspaceAsync();
        }, location);

    private NavigationStop? RelocateFolderNavigation(NavigationStop? stop, string source, string target)
    {
        if (stop?.Viewer is { } viewer)
        {
            var updatedViewer = viewer with { Path = RelocatedFolderPath(viewer.Path, source, target), Parent = RelocateFolderNavigation(viewer.Parent, source, target) };
            return updatedViewer == viewer ? stop : CreateViewerNavigationStop(updatedViewer);
        }
        if (stop?.Folder is not { } location) return stop;
        var updated = location with
        {
            Root = RelocatedFolderPath(location.Root, source, target)!,
            Directory = RelocatedFolderPath(location.Directory, source, target)!,
            Parent = RelocateFolderNavigation(location.Parent, source, target)
        };
        if (!MS3DPRINT.Manager.Core.Workspace.WorkspaceEntityPaths.IsInside(updated.Root, updated.Directory))
            updated = updated with { Root = target };
        return updated == location ? stop : CreateFolderNavigationStop(updated);
    }

    private async Task<bool> NavigateHistoryAsync(int direction)
    {
        EnsureNavigationWorkspace();
        if (_panel is not null || _historyNavigationBusy || direction is not (-1 or 1)) return false;
        var next = _navigationPosition + direction;
        if (next < 0 || next >= _navigationHistory.Count) return false;
        _historyNavigationBusy = _replayingNavigation = true;
        SearchResultsPanel.IsVisible = false;
        try
        {
            await _navigationHistory[next].Restore();
            _navigationPosition = next;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify("Impossible de revenir à cette page : " + exception.Message, error: true); return false; }
        finally { _replayingNavigation = _historyNavigationBusy = false; }
    }

    private async void HistoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_historyNavigationBusy) { e.Handled = true; return; }
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)) return;
        e.Handled = true;
        await NavigateHistoryAsync(kind == PointerUpdateKind.XButton1Pressed ? -1 : 1);
    }

    private async void HistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (_historyNavigationBusy) { e.Handled = true; return; }
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Left or Key.Right)) return;
        e.Handled = true;
        await NavigateHistoryAsync(e.Key == Key.Left ? -1 : 1);
    }
}

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, long> _workspaceLoadVersions = [];
    private readonly record struct WorkspaceLoad(string Surface, string Root, long Version, string? NavigationKey, bool TrackNavigation);

    private WorkspaceLoad StartWorkspaceLoad(string surface, string root, bool trackNavigation = true)
    {
        var version = _workspaceLoadVersions.GetValueOrDefault(surface) + 1;
        _workspaceLoadVersions[surface] = version;
        return new(surface, root, version, CurrentNavigationStop?.Key, trackNavigation);
    }

    private bool IsWorkspaceLoadCurrent(WorkspaceLoad load)
        => _workspaceLoadVersions.GetValueOrDefault(load.Surface) == load.Version
            && string.Equals(load.Root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            && (!load.TrackNavigation || CurrentNavigationStop?.Key == load.NavigationKey);
}

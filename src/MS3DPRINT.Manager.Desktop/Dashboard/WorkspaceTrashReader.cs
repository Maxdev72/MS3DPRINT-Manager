using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Exposes the existing managed trash in read-only mode.</summary>
public sealed class WorkspaceTrashReader
{
    public IReadOnlyList<string> ReadErrors { get; private set; } = [];

    public IReadOnlyList<TrashEntry> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var service = new ManagedFileService(workspaceRoot);
        var entries = service.ListTrash();
        ReadErrors = service.TrashReadErrors;
        return entries;
    }

    public void Restore(string workspaceRoot, Guid id)
        => new ManagedFileService(workspaceRoot).Restore(id);
}

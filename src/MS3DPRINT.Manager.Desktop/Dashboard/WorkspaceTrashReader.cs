using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Exposes the existing managed trash in read-only mode.</summary>
public sealed class WorkspaceTrashReader
{
    public IReadOnlyList<TrashEntry> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return new ManagedFileService(workspaceRoot).ListTrash();
    }

    public void Restore(string workspaceRoot, Guid id)
        => new ManagedFileService(workspaceRoot).Restore(id);
}

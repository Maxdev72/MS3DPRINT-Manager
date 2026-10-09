using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Provides storage configuration choices without persisting changes.</summary>
public sealed class WorkspaceStorageReader
{
    public IReadOnlyList<StorageProviderDefinition> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return StorageProviderCatalog.Create(Path.GetFullPath(workspaceRoot));
    }
}

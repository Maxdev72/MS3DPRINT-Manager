using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Lists the storage places of this computer without persisting anything.</summary>
public sealed class WorkspaceStorageReader
{
    public IReadOnlyList<StorageProviderDefinition> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return StorageProviderCatalog.Create(Path.GetFullPath(workspaceRoot), SyncFolderDetector.ForThisComputer().Detect());
    }
}

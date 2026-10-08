using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Reads the client catalog without creating or updating metadata.</summary>
public sealed class WorkspaceClientReader
{
    public IReadOnlyList<ClientSummary> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        return new ClientCatalog(new ClientProfileStore(paths)).Load(paths.RootPath);
    }
}

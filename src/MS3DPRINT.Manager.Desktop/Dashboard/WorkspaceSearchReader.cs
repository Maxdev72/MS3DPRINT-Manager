using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Search;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Adapts the existing workspace search for the desktop shell without writing data.</summary>
public sealed class WorkspaceSearchReader
{
    public IReadOnlyList<GlobalSearchResult> Search(string workspaceRoot, string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var clients = new ClientCatalog(new ClientProfileStore(paths));
        var projects = new ProjectCatalog(new ProjectProfileStore(paths));
        return new GlobalSearchService(clients, projects)
            .Search(paths.RootPath, query, cancellationToken: cancellationToken)
            .Results;
    }
}

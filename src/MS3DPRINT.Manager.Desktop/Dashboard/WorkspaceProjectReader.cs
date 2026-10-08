using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Reads the project catalog while preserving the current workspace files.</summary>
public sealed class WorkspaceProjectReader
{
    public IReadOnlyList<ProjectSummary> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        return new ProjectCatalog(new ProjectProfileStore(paths)).Load(paths.RootPath);
    }
}

using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Creates a project through the existing atomic workspace service.</summary>
public sealed class WorkspaceProjectCreator
{
    public ProjectReference Create(string clientPath, string clientCode, int year, string projectName)
        => new ProjectCreationService(new FolderTreeService()).Create(clientPath, clientCode, year, projectName);
}

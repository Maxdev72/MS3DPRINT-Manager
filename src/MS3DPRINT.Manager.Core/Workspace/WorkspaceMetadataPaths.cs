namespace MS3DPRINT.Manager.Core.Workspace;

public sealed class WorkspaceMetadataPaths
{
    public WorkspaceMetadataPaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        RootPath = Path.GetFullPath(rootPath);
        MetadataDirectory = Path.Combine(RootPath, ".ms3dprint-manager");
        ClientsDirectory = Path.Combine(MetadataDirectory, "clients");
        ProjectsDirectory = Path.Combine(MetadataDirectory, "projects");
        HistoryDirectory = Path.Combine(MetadataDirectory, "history");
    }

    public string RootPath { get; }
    public string MetadataDirectory { get; }
    public string ClientsDirectory { get; }
    public string ProjectsDirectory { get; }
    public string HistoryDirectory { get; }

    public void EnsureMetadataDirectories()
    {
        Directory.CreateDirectory(ClientsDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
    }
}

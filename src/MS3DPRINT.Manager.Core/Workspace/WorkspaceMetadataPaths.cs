namespace MS3DPRINT.Manager.Core.Workspace;

public sealed class WorkspaceMetadataPaths
{
    public WorkspaceMetadataPaths(string rootPath) : this(rootPath, null) { }

    /// <summary>Documents under <paramref name="rootPath"/>, records under <paramref name="metadataRoot"/> (historical <c>.ms3dprint-manager</c> when null).</summary>
    public WorkspaceMetadataPaths(string rootPath, string? metadataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        RootPath = Path.GetFullPath(rootPath);
        MetadataDirectory = metadataRoot is null ? Path.Combine(RootPath, ".ms3dprint-manager") : Path.GetFullPath(metadataRoot);
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

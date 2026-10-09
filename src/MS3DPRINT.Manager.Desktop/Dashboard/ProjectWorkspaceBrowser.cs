using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Read-only project file browsing for the Avalonia workspace.</summary>
public sealed class ProjectWorkspaceBrowser
{
    private readonly ProjectFileBrowser _browser = new();

    public IReadOnlyList<ProjectFileEntry> List(string projectPath, string? directory = null)
        => _browser.List(projectPath, directory ?? projectPath);

    public string Parent(string projectPath, string directory)
        => _browser.GetParentDirectory(projectPath, directory);
}

using MS3DPRINT.Manager.Core.Search;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceSearchReaderTests
{
    [Fact]
    public void Search_finds_projects_from_the_existing_workspace_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-search-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(root, "01_CLIENTS", "ACME", "ACME-2026-001_Test fixture");
        Directory.CreateDirectory(projectDirectory);

        try
        {
            var results = new WorkspaceSearchReader().Search(root, "fixture");

            Assert.Contains(results, result => result.Kind == GlobalSearchResultKind.Project && result.Title.Contains("Test fixture", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

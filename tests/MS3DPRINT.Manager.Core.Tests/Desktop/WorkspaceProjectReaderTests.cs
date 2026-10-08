using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceProjectReaderTests
{
    [Fact]
    public void Load_returns_projects_from_the_existing_workspace_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-projects-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME", "ACME-2026-001_Test fixture"));

        try
        {
            var projects = new WorkspaceProjectReader().Load(root);

            var project = Assert.Single(projects);
            Assert.Equal("ACME-2026-001", project.Reference);
            Assert.Equal("Test fixture", project.ProjectName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

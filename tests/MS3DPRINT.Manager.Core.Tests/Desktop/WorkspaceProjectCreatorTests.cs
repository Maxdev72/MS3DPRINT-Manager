using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceProjectCreatorTests
{
    [Fact]
    public void Create_builds_the_standard_project_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-create-{Guid.NewGuid():N}");
        var client = Path.Combine(root, "01_CLIENTS", "ACME");
        Directory.CreateDirectory(client);
        try
        {
            var project = new WorkspaceProjectCreator().Create(client, "ACME", 2026, "Support bobine");
            Assert.True(Directory.Exists(Path.Combine(client, project.FolderName)));
        }
        finally { Directory.Delete(root, true); }
    }
}

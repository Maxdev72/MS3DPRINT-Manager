using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceDashboardReaderTests
{
    [Fact]
    public void Load_returns_an_empty_dashboard_without_creating_workspace_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-dashboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var dashboard = new WorkspaceDashboardReader().Load(root);

            Assert.Equal(0, dashboard.ClientCount);
            Assert.Equal(0, dashboard.ProjectCount);
            Assert.Empty(dashboard.RecentProjects);
            Assert.False(Directory.Exists(Path.Combine(root, ".ms3dprint-manager")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

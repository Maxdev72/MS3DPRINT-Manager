using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ProjectWorkspaceBrowserTests
{
    [Fact]
    public void List_reads_files_without_leaving_the_project()
    {
        var project = Path.Combine(Path.GetTempPath(), $"ms3dprint-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "plan.txt"), "ok");
        try { Assert.Equal("plan.txt", Assert.Single(new ProjectWorkspaceBrowser().List(project)).Name); }
        finally { Directory.Delete(project, true); }
    }
}

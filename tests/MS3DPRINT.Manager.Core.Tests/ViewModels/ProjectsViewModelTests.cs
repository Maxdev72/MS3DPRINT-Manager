using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ProjectsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-vm-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void VisibleProjects_FiltersByTextAndStatus()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var store = new ProjectProfileStore(paths);
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote, DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", profile.FolderName));
        var viewModel = new ProjectsViewModel(new ProjectCatalog(store), _root);

        viewModel.Refresh();
        viewModel.SearchText = "outillage";
        viewModel.SelectedStatus = ProjectStatus.Quote;

        Assert.Equal("MPO-2026-001", Assert.Single(viewModel.VisibleProjects).Reference);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

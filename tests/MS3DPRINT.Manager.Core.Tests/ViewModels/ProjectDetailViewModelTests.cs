using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ProjectDetailViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-detail-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_UpdatesOnlyTrackingFieldsOfTheSelectedProject()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var viewModel = new ProjectDetailViewModel(profile, store)
        {
            Status = ProjectStatus.InProgress,
            DueDate = new DateTime(2026, 10, 15),
            Description = "Prototype validé",
            Notes = "Prévoir le contrôle final"
        };

        viewModel.Save();

        var saved = store.LoadAll().Single();
        Assert.Equal(ProjectStatus.InProgress, saved.Status);
        Assert.Equal(new DateOnly(2026, 10, 15), saved.DueDate);
        Assert.Equal("Prototype validé", saved.Description);
        Assert.Equal("Prévoir le contrôle final", saved.Notes);
        Assert.Equal(profile.Reference, saved.Reference);
    }

    [Fact]
    public void ReadFiles_DefersFileListChangesUntilTheResultIsApplied()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var projectPath = Path.Combine(_root, "01_CLIENTS", "MPO", profile.FolderName);
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(Path.Combine(projectPath, "brief.pdf"), "x");
        var summary = new ProjectSummary("MPO", Path.GetDirectoryName(projectPath)!, projectPath, profile.Reference, profile.FolderName, profile);
        var viewModel = new ProjectDetailViewModel(summary, store);

        var listing = viewModel.ReadFiles();

        Assert.Empty(viewModel.FileEntries);
        viewModel.ApplyFileListing(listing);
        Assert.Equal("brief.pdf", Assert.Single(viewModel.FileEntries).Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

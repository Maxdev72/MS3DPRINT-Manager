using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ProjectDetailViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-detail-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void UnsavedChanges_ClearsAfterSaveAndIgnoresFileBrowsing()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "AT", "AT-2026-001", "AT-2026-001_TEST", "TEST", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var model = new ProjectDetailViewModel(profile, store);
        var property = typeof(ProjectDetailViewModel).GetProperty("HasUnsavedChanges");
        Assert.NotNull(property);
        bool Dirty() => (bool)property.GetValue(model)!;

        Assert.False(Dirty());
        model.ApplyFileListing(new ProjectFileListing("C:\\test", []));
        Assert.False(Dirty());
        model.Status = ProjectStatus.InProgress;
        Assert.True(Dirty());
        model.Save();
        Assert.False(Dirty());
    }

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

    [Fact]
    public void ReadFiles_ReturnsToProjectRootWhenTheOpenedSubdirectoryWasRemoved()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var projectPath = Path.Combine(_root, "01_CLIENTS", "MPO", profile.FolderName);
        var openedDirectory = Path.Combine(projectPath, "03_CAO_3D");
        Directory.CreateDirectory(openedDirectory);
        File.WriteAllText(Path.Combine(projectPath, "brief.pdf"), "x");
        var summary = new ProjectSummary("MPO", Path.GetDirectoryName(projectPath)!, projectPath, profile.Reference, profile.FolderName, profile);
        var viewModel = new ProjectDetailViewModel(summary, store);

        viewModel.ApplyFileListing(viewModel.ReadFilesForDirectory(new ProjectFileEntry("03_CAO_3D", openedDirectory, true, null, DateTimeOffset.UtcNow)));
        Directory.Delete(openedDirectory);

        var listing = viewModel.ReadFiles();

        Assert.Equal(projectPath, listing.DirectoryPath);
        Assert.Equal("brief.pdf", Assert.Single(listing.Entries).Name);
    }

    [Fact]
    public void ApplyFolderSizes_UpdatesOnlyDirectoriesInCurrentListing()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_TEST", "TEST", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var model = new ProjectDetailViewModel(profile, store);
        model.ApplyFileListing(new ProjectFileListing("C:\\test", [
            new ProjectFileEntry("DOSSIER", "C:\\test\\DOSSIER", true, null, DateTimeOffset.UtcNow),
            new ProjectFileEntry("brief.pdf", "C:\\test\\brief.pdf", false, 12, DateTimeOffset.UtcNow)]));

        model.ApplyFolderSizes(new Dictionary<string, long?> { ["C:\\test\\DOSSIER"] = 1536 });

        Assert.Equal(1536, model.FileEntries.Single(entry => entry.IsDirectory).Length);
        Assert.Equal(12, model.FileEntries.Single(entry => !entry.IsDirectory).Length);
    }

    [Fact]
    public void CurrentPathLabel_ShowsShortProjectBreadcrumb()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_TEST", "TEST", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);
        store.Create(profile);
        var project = Path.Combine(_root, "01_CLIENTS", "MPO", profile.FolderName);
        var model = new ProjectDetailViewModel(new ProjectSummary("MPO", Path.GetDirectoryName(project)!, project, profile.Reference, profile.FolderName, profile), store);

        model.ApplyFileListing(new ProjectFileListing(Path.Combine(project, "01_DEVIS_FACTURES"), []));

        Assert.Equal("MPO-2026-001_TEST › 01_DEVIS_FACTURES", model.CurrentPathLabel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

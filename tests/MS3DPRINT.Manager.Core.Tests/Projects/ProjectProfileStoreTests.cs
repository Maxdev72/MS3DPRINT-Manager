using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-profile-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Priority_roundtrips_and_other_project_edits_preserve_it()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        store.Create(CreateProfile() with { Priority = ProjectPriority.High });
        var profile = store.LoadAll().Single();
        Assert.Equal(ProjectPriority.High, profile.Priority);
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProfileEditor().UpdateProject(_root, profile, ProjectStatus.InProgress, null, "Description", "Notes");
        Assert.Equal(ProjectPriority.High, store.LoadAll().Single().Priority);
    }

    [Fact]
    public void Missing_priority_in_existing_json_defaults_to_none()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var store = new ProjectProfileStore(paths);
        store.Create(CreateProfile());
        var file = Directory.GetFiles(paths.ProjectsDirectory, "*.json").Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json.Remove("Priority"); File.WriteAllText(file, json.ToJsonString());
        Assert.Equal(ProjectPriority.None, store.LoadAll().Single().Priority);
    }

    [Fact]
    public void Unknown_priority_cannot_be_saved()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        Assert.Throws<ArgumentException>(() => store.Create(CreateProfile() with { Priority = (ProjectPriority)99 }));
    }

    [Fact]
    public void Create_PersistsAQuoteProfile()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        store.Create(CreateProfile());

        var profile = store.LoadAll().Single();
        Assert.Equal(ProjectStatus.Quote, profile.Status);
        Assert.Equal("MPO-2026-001", profile.Reference);
    }

    [Fact]
    public void Create_RejectsASecondProfileForTheSameProjectFolder()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = CreateProfile();
        store.Create(profile);

        Assert.Throws<InvalidOperationException>(() =>
            store.Create(profile with { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Update_ArchivesThePriorProfile()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var store = new ProjectProfileStore(paths);
        var profile = CreateProfile();
        store.Create(profile);

        store.Update(profile with { Status = ProjectStatus.InProgress, UpdatedAt = profile.UpdatedAt.AddMinutes(1) });

        Assert.Equal(ProjectStatus.InProgress, store.LoadAll().Single().Status);
        Assert.Single(Directory.GetFiles(paths.HistoryDirectory, "*.json"));
    }

    private static ProjectProfile CreateProfile() => new(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote, DateTimeOffset.UtcNow, null, "Prototype", null, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

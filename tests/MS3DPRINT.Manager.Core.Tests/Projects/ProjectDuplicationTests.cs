using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectDuplicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3dprint-copy-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private (MS3DPRINT.Manager.Core.Clients.ClientSummary Client, ProjectSummary Project) Original()
    {
        Directory.CreateDirectory(_root);
        new WorkspaceClientCreator().CreateProfessional(_root, "Atelier", "ATELIER");
        var client = new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Original", ProjectPriority.High);
        var project = new WorkspaceProjectReader().Load(_root).Single();
        var profile = project.Profile! with { Notes = "Notes utiles", Description = "Dimensions à conserver", DueDate = new DateOnly(2026, 10, 1), Status = ProjectStatus.Completed };
        new ProjectProfileStore(new WorkspaceMetadataPaths(_root)).Update(profile);
        File.WriteAllText(Path.Combine(project.ProjectPath, "piece.stl"), "original");
        return (client, project with { Profile = profile });
    }

    [Fact]
    public void Metadata_copy_keeps_notes_priority_and_source_but_resets_schedule_and_status()
    {
        var (client, source) = Original();
        var copy = new ProjectDuplicationService().Duplicate(_root, source, client, "Copie", false);
        Assert.NotEqual(source.Profile!.Id, copy.Profile!.Id); Assert.NotEqual(source.Reference, copy.Reference);
        Assert.Equal(ProjectStatus.Quote, copy.Profile.Status); Assert.Null(copy.Profile.DueDate);
        Assert.Equal(source.Profile.Description, copy.Profile.Description); Assert.Equal(source.Profile.Notes, copy.Profile.Notes);
        Assert.Equal(ProjectPriority.High, copy.Priority);
        Assert.False(File.Exists(Path.Combine(copy.ProjectPath, "piece.stl")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(source.ProjectPath, "piece.stl")));
    }

    [Fact]
    public void Failed_file_copy_preserves_source_and_moves_incomplete_creation_out_of_active_projects()
    {
        var (client, source) = Original();
        using (var locked = new FileStream(Path.Combine(source.ProjectPath, "piece.stl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => new ProjectDuplicationService().Duplicate(_root, source, client, "Copie interrompue", true));
        Assert.Single(new WorkspaceProjectReader().Load(_root));
        Assert.Single(new ProjectProfileStore(new WorkspaceMetadataPaths(_root)).LoadAll());
        Assert.Equal("original", File.ReadAllText(Path.Combine(source.ProjectPath, "piece.stl")));
    }

    [Fact]
    public void Foreign_source_is_rejected_before_creating_another_project()
    {
        var (client, source) = Original();
        Assert.Throws<ArgumentException>(() => new ProjectDuplicationService().Duplicate(_root, source with { ProjectPath = Path.GetTempPath() }, client, "Refusé", true));
        Assert.Single(new WorkspaceProjectReader().Load(_root));
    }
}

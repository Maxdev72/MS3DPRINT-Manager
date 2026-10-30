using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectTasksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "manager-tasks-" + Guid.NewGuid());
    private ProjectProfileStore Store => new(new WorkspaceMetadataPaths(_root));
    private ProjectProfile Create()
    {
        var p = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "CT", "CT-2026-001", "CT-2026-001_TEST", "Test", ProjectStatus.Quote, DateTimeOffset.UtcNow, null, null, "Notes du projet", DateTimeOffset.UtcNow);
        Store.Create(p); return p;
    }

    [Fact]
    public void Tasks_roundtrip_with_notes_and_completion_without_changing_project_status()
    {
        var p = Create();
        p = ProjectTasks.Add(Store, p, "  Modéliser  ", "Cote à vérifier");
        p = ProjectTasks.Add(Store, p, "Livrer", null);
        p = ProjectTasks.SetCompleted(Store, p, p.Tasks![0].Id, true);
        var saved = Store.LoadAll().Single();
        Assert.Equal("Modéliser", saved.Tasks![0].Title);
        Assert.Equal("Cote à vérifier", saved.Tasks[0].Notes);
        Assert.True(saved.Tasks[0].IsCompleted);
        Assert.Equal("Livrer", ProjectTasks.Next(saved)!.Title);
        Assert.Equal(ProjectStatus.Quote, saved.Status);
        Assert.Equal("Notes du projet", saved.Notes);
        p = ProjectTasks.SetCompleted(Store, saved, saved.Tasks[0].Id, false);
        Assert.Equal("Modéliser", ProjectTasks.Next(p)!.Title);
    }

    [Fact]
    public void Reorder_edit_and_remove_preserve_task_identity_and_other_fields()
    {
        var p = ProjectTasks.Add(Store, Create(), "Un", null);
        p = ProjectTasks.Add(Store, p, "Deux", null);
        var id = p.Tasks![1].Id;
        p = ProjectTasks.Move(Store, p, id, -1);
        Assert.Equal(id, p.Tasks![0].Id);
        p = ProjectTasks.Edit(Store, p, id, "Deux corrigé", "Une note");
        Assert.Equal("Deux corrigé", ProjectTasks.Next(p)!.Title);
        p = ProjectTasks.Remove(Store, p, id);
        Assert.Equal("Un", Assert.Single(p.Tasks!).Title);
    }

    [Fact]
    public void Stale_task_write_cannot_erase_a_concurrent_project_edit()
    {
        var p = Create();
        Store.Update(p with { Notes = "Modifié ailleurs" }, p.UpdatedAt);
        Assert.Throws<ProfileConflictException>(() => ProjectTasks.Add(Store, p, "Tâche", null));
        Assert.Equal("Modifié ailleurs", Store.LoadAll().Single().Notes);
        Assert.Null(Store.LoadAll().Single().Tasks);
    }

    [Fact]
    public void Old_profiles_have_no_tasks_and_other_edits_keep_existing_tasks()
    {
        var p = Create();
        Assert.Null(ProjectTasks.Next(p));
        p = ProjectTasks.Add(Store, p, "Relancer", null);
        Store.Update(p with { ProjectName = "Renommé", Priority = ProjectPriority.High }, p.UpdatedAt);
        Assert.Equal("Relancer", ProjectTasks.Next(Store.LoadAll().Single())!.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_tasks_are_rejected_without_writing(string title)
    {
        var p = Create();
        Assert.Throws<ArgumentException>(() => ProjectTasks.Add(Store, p, title, null));
        Assert.Null(Store.LoadAll().Single().Tasks);
    }

    [Fact]
    public void A_corrupt_task_is_reported_as_unreadable_and_the_file_is_preserved()
    {
        var p = Create();
        var path = Path.Combine(new WorkspaceMetadataPaths(_root).ProjectsDirectory, p.Id + ".json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["Tasks"] = System.Text.Json.Nodes.JsonNode.Parse("[null]");
        File.WriteAllText(path, json.ToJsonString());
        var before = File.ReadAllBytes(path);
        Assert.Throws<System.Text.Json.JsonException>(() => Store.LoadAll());
        Assert.Empty(Store.LoadReadable());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Drag_reorders_relative_to_a_target_and_preserves_hidden_completed_tasks(bool before)
    {
        var p=ProjectTasks.Add(Store,Create(),"Un",null);
        p=ProjectTasks.Add(Store,p,"Fait","Note conservée");
        p=ProjectTasks.Add(Store,p,"Deux",null);
        p=ProjectTasks.Add(Store,p,"Trois",null);
        p=ProjectTasks.SetCompleted(Store,p,p.Tasks![1].Id,true);
        var original=p;
        p=ProjectTasks.MoveRelative(Store,p,p.Tasks![3].Id,p.Tasks[0].Id,before);
        Assert.Equal(before ? new[]{"Trois","Un","Fait","Deux"} : new[]{"Un","Trois","Fait","Deux"},p.Tasks!.Select(t=>t.Title));
        Assert.Equal("Note conservée",p.Tasks!.Single(t=>t.IsCompleted).Notes);
        Assert.Equal(ProjectStatus.Quote,p.Status);
        Assert.Throws<ProfileConflictException>(()=>ProjectTasks.MoveRelative(Store,original,original.Tasks![0].Id,original.Tasks[2].Id,true));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

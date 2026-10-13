using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

[Collection("WorkspaceEnvironment")]
public sealed class WorkspaceLocationTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "ms3d-location-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable(WorkspaceLocation.EnvironmentVariable);

    public WorkspaceLocationTests() => Environment.SetEnvironmentVariable(WorkspaceLocation.EnvironmentVariable, null);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(WorkspaceLocation.EnvironmentVariable, _previous);
        if (Directory.Exists(_data)) Directory.Delete(_data, true);
    }

    [Fact]
    public void Without_any_choice_the_default_root_is_used()
        => Assert.Equal(WorkspaceLocation.DefaultRoot, new WorkspaceLocation(_data).Resolve());

    [Fact]
    public void A_saved_folder_is_found_by_a_new_instance()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_data, "espace")).FullName;
        new WorkspaceLocation(_data).Save(workspace + Path.DirectorySeparatorChar);

        Assert.Equal(workspace, new WorkspaceLocation(_data).Resolve());
    }

    [Fact]
    public void The_environment_variable_wins_over_the_saved_folder()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_data, "espace")).FullName;
        new WorkspaceLocation(_data).Save(workspace);
        Environment.SetEnvironmentVariable(WorkspaceLocation.EnvironmentVariable, @"D:\autre");

        Assert.Equal(@"D:\autre", new WorkspaceLocation(_data).Resolve());
        Assert.True(WorkspaceLocation.IsFixedByEnvironment);
    }

    [Fact]
    public void A_missing_folder_cannot_be_saved()
        => Assert.Throws<DirectoryNotFoundException>(() => new WorkspaceLocation(_data).Save(Path.Combine(_data, "absent")));

    [Fact]
    public void A_saved_folder_that_disappeared_falls_back_to_the_default()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_data, "espace")).FullName;
        new WorkspaceLocation(_data).Save(workspace);
        Directory.Delete(workspace);

        Assert.Equal(WorkspaceLocation.DefaultRoot, new WorkspaceLocation(_data).Resolve());
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_default()
    {
        Directory.CreateDirectory(_data);
        File.WriteAllText(Path.Combine(_data, "workspace.json"), "{ pas du json");

        Assert.Equal(WorkspaceLocation.DefaultRoot, new WorkspaceLocation(_data).Resolve());
    }
}

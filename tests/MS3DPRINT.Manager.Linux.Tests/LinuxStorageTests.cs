using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Linux.Tests;

public sealed class LinuxStorageTests
{
    [Fact]
    public void A_new_XDG_data_directory_does_not_fall_back_to_a_relative_path()
    {
        if (!OperatingSystem.IsLinux()) return;
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var missing = Path.Combine(Path.GetTempPath(), "ms3d-new-xdg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", missing);
            Assert.Equal(Path.Combine(missing, "MS3DPRINT Manager", "data"), ApplicationPaths.DataDirectory);
        }
        finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous); }
    }
    [Fact]
    public void Linux_default_workspace_is_in_the_users_home()
    {
        if (!OperatingSystem.IsLinux()) return;
        var actual = WorkspaceLocation.DefaultRoot;
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MS3DPRINT"), actual);
        Assert.True(Path.IsPathFullyQualified(WorkspaceLocation.DefaultRoot));
    }

    [Fact]
    public void Linux_settings_are_saved_in_user_data_instead_of_the_application_directory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("ms3d-linux-settings-").FullName;
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "data", "workspace.json");
        Assert.False(File.Exists(settings)); // This test must run with an isolated XDG_DATA_HOME.
        try
        {
            new WorkspaceLocation().Save(root);
            Assert.True(File.Exists(settings));
            Assert.Equal(root, new WorkspaceLocation().Resolve());
        }
        finally { Directory.Delete(root); if (File.Exists(settings)) File.Delete(settings); }
    }
}

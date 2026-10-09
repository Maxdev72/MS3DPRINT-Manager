using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceFilamentEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3dprint-filament-editor-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_persists_a_new_filament()
    {
        var profile = new WorkspaceFilamentEditor().Create(_root, "Prusa", "Galaxy Black", "PLA", 24.90m, false);

        Assert.Single(new WorkspaceInventoryReader().LoadFilaments(_root));
        Assert.Equal("Galaxy Black", profile.Name);
    }

    [Fact]
    public void Update_persists_inventory_changes()
    {
        var editor = new WorkspaceFilamentEditor();
        var profile = editor.Create(_root, "Prusa", "Galaxy Black", "PLA", 24.90m, false);

        var updated = editor.Update(_root, profile, "Prusa", "Galaxy Black", "PETG", 27.50m, true);

        Assert.Equal("PETG", updated.Material);
        Assert.True(new WorkspaceInventoryReader().LoadFilaments(_root).Single().IsAbrasive);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

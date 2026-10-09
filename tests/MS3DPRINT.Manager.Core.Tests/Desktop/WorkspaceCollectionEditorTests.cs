using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceCollectionEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3dprint-collection-editor-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_creates_a_profile_for_an_existing_collection_folder()
    {
        var folder = Path.Combine(_root, "02_MODELES_3D", "Support");
        Directory.CreateDirectory(folder);
        var item = new CollectionItemSummary("Support", folder, DateTimeOffset.UtcNow);

        var saved = new WorkspaceCollectionEditor().Save(_root, item, "02_MODELES_3D", "Support bobine", "Pièce imprimable", "À vérifier");

        Assert.Equal("Support bobine", saved.Name);
        Assert.Equal("Pièce imprimable", new CollectionProfileStore(_root).FindByPath(folder)!.Description);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

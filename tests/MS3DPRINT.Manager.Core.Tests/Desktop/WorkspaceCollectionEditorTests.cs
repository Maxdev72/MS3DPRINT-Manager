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

    [Fact]
    public void Save_persists_supplier_contact_information()
    {
        var folder = Path.Combine(_root, "06_FOURNISSEURS", "Filament Pro");
        Directory.CreateDirectory(folder);
        var item = new CollectionItemSummary("Filament Pro", folder, DateTimeOffset.UtcNow);

        var saved = new WorkspaceCollectionEditor().Save(_root, item, "06_FOURNISSEURS", "Filament Pro", null, null,
            contact: "Camille Durand", address: "8 rue des Ateliers", phone: "0600000000", email: "contact@example.test", website: "https://example.test");

        Assert.Equal("Camille Durand", saved.Contact);
        var stored = new CollectionProfileStore(_root).FindByPath(folder)!;
        Assert.Equal("contact@example.test", stored.Email);
        Assert.Equal("https://example.test", stored.Website);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

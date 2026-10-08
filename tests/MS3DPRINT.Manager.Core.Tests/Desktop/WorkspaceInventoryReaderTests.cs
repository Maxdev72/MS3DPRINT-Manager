using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceInventoryReaderTests
{
    [Fact]
    public void Load_collections_returns_legacy_collection_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "02_MODELES_3D", "Pièces"));
        try { Assert.Equal("Pièces", Assert.Single(new WorkspaceInventoryReader().LoadCollections(root, "02_MODELES_3D")).Name); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Load_filaments_does_not_create_storage_when_absent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-filaments-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { Assert.Empty(new WorkspaceInventoryReader().LoadFilaments(root)); Assert.False(Directory.Exists(Path.Combine(root, ".ms3dprint-manager"))); }
        finally { Directory.Delete(root, true); }
    }
}

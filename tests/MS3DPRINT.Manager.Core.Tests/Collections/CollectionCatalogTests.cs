using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Core.Tests.Collections;

public sealed class CollectionCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-collection-catalog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsOnlyBusinessFoldersOrderedByName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "ZEBRA"));
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "ALPHA"));
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", ".MS3DPRINT-STAGING-TEMP"));

        var items = new CollectionCatalog().Load(_root, "02_MODELES_3D");

        Assert.Equal(new[] { "ALPHA", "ZEBRA" }, items.Select(item => item.Name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_KeepsLegacyDocumentsBelowGroupingButDoesNotCountIndexedDocumentsAsLegacy()
    {
        var grouping = Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "GROUP")).FullName;
        var indexed = Directory.CreateDirectory(Path.Combine(grouping, "INDEXED")).FullName;
        File.WriteAllText(Path.Combine(indexed, "tracked.pdf"), "tracked");
        var now = DateTimeOffset.UtcNow;
        new MS3DPRINT.Manager.Core.Collections.CollectionProfileStore(_root).Create(new(Guid.NewGuid(), "06_FOURNISSEURS", "06_FOURNISSEURS/GROUP/INDEXED", "Indexed", null, null, null, null, null, null, null, now, now));
        Assert.DoesNotContain(new CollectionCatalog().Load(_root, "06_FOURNISSEURS"), item => item.Path == grouping);
        var documents = Directory.CreateDirectory(Path.Combine(grouping, "DOCS", "TARIFS")).FullName;
        File.WriteAllText(Path.Combine(documents, "legacy.pdf"), "legacy");
        var result = new CollectionCatalog().Load(_root, "06_FOURNISSEURS");
        Assert.Contains(result, item => item.Path == grouping && item.Profile is null);
        Assert.Contains(result, item => item.Path == indexed && item.Profile is not null);
    }
}

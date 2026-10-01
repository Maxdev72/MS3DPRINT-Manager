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
}

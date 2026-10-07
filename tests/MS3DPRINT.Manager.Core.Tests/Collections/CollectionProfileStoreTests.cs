using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Core.Tests.Collections;

public sealed class CollectionProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-collection-profile-" + Guid.NewGuid().ToString("N"));
    private CollectionProfile Profile(string path = "06_FOURNISSEURS/LEGACY") => new(Guid.NewGuid(), "06_FOURNISSEURS", path, "Fournisseur", "Description", "Contact", "Adresse", "0123", "a@example.fr", "https://example.fr", "Notes", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public void Create_AdoptsLegacyFolderAndPersistsAllFieldsWithoutRenaming()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "LEGACY")).FullName;
        var profile = Profile();
        new CollectionProfileStore(_root).Create(profile);
        var store = new CollectionProfileStore(_root);
        Assert.Equal(profile, store.Load(profile.Id));
        Assert.Equal(profile, store.FindByPath(folder));
        Assert.Single(store.LoadAll());
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "collections", profile.Id + ".json")));
    }

    [Fact]
    public void Update_PreservesIdentityAndPersistsMovedNestedPath()
    {
        Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "LEGACY"));
        var store = new CollectionProfileStore(_root);
        var original = Profile();
        store.Create(original);
        var moved = Path.Combine(_root, "06_FOURNISSEURS", "Group", "MOVED");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(Path.Combine(_root, "06_FOURNISSEURS", "LEGACY"), moved);
        var updated = original with { RelativePath = "06_FOURNISSEURS/Group/MOVED", Name = "Nouveau nom", Notes = "Modifiées", UpdatedAt = original.UpdatedAt.AddMinutes(1) };
        store.Update(updated);
        Assert.Equal(updated, store.Load(original.Id));
        var item = Assert.Single(new CollectionCatalog().Load(_root, original.Category));
        Assert.Equal(moved, item.Path);
        Assert.Equal(updated, item.Profile);
        Assert.Equal("Nouveau nom", item.Name);
    }

    [Theory]
    [InlineData("devis.pdf")]
    [InlineData("notes.txt")]
    public void Catalog_PreservesLegacyAncestorWithItsOwnDocuments(string documentName)
    {
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "LEGACY")).FullName;
        var document = Path.Combine(legacy, documentName);
        File.WriteAllText(document, "Document du dossier existant");
        var nested = Directory.CreateDirectory(Path.Combine(legacy, "MOVED")).FullName;
        var profile = Profile("06_FOURNISSEURS/LEGACY/MOVED");
        new CollectionProfileStore(_root).Create(profile);

        var catalog = new CollectionCatalog().Load(_root, "06_FOURNISSEURS");
        Assert.Contains(catalog, item => item.Path == legacy && item.Profile is null);
        Assert.Contains(catalog, item => item.Path == nested && item.Profile?.Id == profile.Id);
        Assert.Equal("Document du dossier existant", File.ReadAllText(document));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("06_FOURNISSEURS/../02_MODELES_3D/Folder")]
    [InlineData("02_MODELES_3D/Folder")]
    [InlineData("06_FOURNISSEURS/.MS3DPRINT-STAGING-test")]
    [InlineData("06_FOURNISSEURS")]
    public void Create_RejectsUnsafeOrWrongCategoryPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new CollectionProfileStore(_root).Create(Profile(path)));
        Assert.False(Directory.Exists(Path.Combine(_root, ".ms3dprint-manager")));
    }

    [Fact]
    public void Store_RejectsDuplicatePathAndInvalidUpdateWithoutChangingSavedProfile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "LEGACY"));
        var store = new CollectionProfileStore(_root);
        var original = Profile();
        store.Create(original);
        Assert.Throws<InvalidOperationException>(() => store.Create(Profile()));
        Assert.Throws<ArgumentException>(() => store.Update(original with { Name = " " }));
        Assert.Throws<InvalidOperationException>(() => store.Update(original with { Id = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => store.Update(original with { CreatedAt = original.CreatedAt.AddDays(1) }));
        Assert.Equal(original, store.Load(original.Id));
    }

    [Fact]
    public void Catalog_ExcludesMetadataAndStagingFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", ".ms3dprint-manager"));
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", ".MS3DPRINT-STAGING-temp"));
        Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "LEGACY"));
        Assert.Equal("LEGACY", Assert.Single(new CollectionCatalog().Load(_root, "02_MODELES_3D")).Name);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

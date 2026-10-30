using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceCollectionCreatorTests
{
    [Theory]
    [InlineData("02_MODELES_3D")]
    [InlineData("03_PRODUITS_MS3DPRINT")]
    [InlineData("06_FOURNISSEURS")]
    public void Creation_normalizes_folder_and_preserves_readable_profile_name(string category)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-collection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new WorkspaceCollectionCreator().Create(root, category, "  Pots décoratifs  ");
            Assert.Equal(Path.Combine(root, category, "POTS_DECORATIFS"), item.Path);
            Assert.Equal("Pots décoratifs", item.Name);
            new WorkspaceCollectionEditor().Save(root, item, category, item.Name, null, null);
            var profile = new MS3DPRINT.Manager.Core.Collections.CollectionProfileStore(root).FindByPath(item.Path);
            Assert.Equal("Pots décoratifs", profile!.Name);
            File.WriteAllText(Path.Combine(item.Path, "original.txt"), "conservé");
            Assert.ThrowsAny<IOException>(() => new WorkspaceCollectionCreator().Create(root, category, "pots-decoratifs"));
            Assert.Equal("conservé", File.ReadAllText(Path.Combine(item.Path, "original.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("...")]
    [InlineData("   ")]
    public void Creation_rejects_names_without_letters_or_digits_before_writing(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-collection-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<ArgumentException>(() => new WorkspaceCollectionCreator().Create(root, "02_MODELES_3D", name));
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Create_model_creates_its_standard_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new WorkspaceCollectionCreator().Create(root, "02_MODELES_3D", "Support bobine");

            Assert.True(Directory.Exists(item.Path));
            Assert.True(Directory.Exists(Path.Combine(item.Path, "05_STL")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

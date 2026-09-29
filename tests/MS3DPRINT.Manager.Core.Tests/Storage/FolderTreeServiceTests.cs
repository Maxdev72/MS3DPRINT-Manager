using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class FolderTreeServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FolderTreeService _service = new();

    [Fact]
    public void EnsureMainStructure_CreatesEveryRequiredFolderWithoutChangingExistingContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS"));
        var marker = Path.Combine(_root, "01_CLIENTS", "existing.txt");
        File.WriteAllText(marker, "keep");

        _service.EnsureMainStructure(_root);

        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.All(new[]
        {
            "01_CLIENTS", "02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "04_COMMUNICATION",
            "05_MACHINES_MATERIAUX", "06_FOURNISSEURS", "07_ADMINISTRATIF",
            "08_COMPTABILITE", "09_COMMERCIAL", "10_RESSOURCES_MS3DPRINT", "99_ARCHIVES"
        }, folder => Assert.True(Directory.Exists(Path.Combine(_root, folder)), folder));
        Assert.Equal(11, FolderTemplates.Main.Count);
    }

    [Fact]
    public void CreateTree_ThrowsBeforeWritingWhenDestinationDirectoryExists()
    {
        var target = Path.Combine(_root, "EXISTANT");
        Directory.CreateDirectory(target);
        var marker = Path.Combine(target, "existing.txt");
        File.WriteAllText(marker, "keep");

        Assert.Throws<FolderConflictException>(() => _service.CreateTree(target, FolderTemplates.Model));

        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.Empty(Directory.GetDirectories(target));
        Assert.Empty(Directory.GetDirectories(_root, ".MS3DPRINT-STAGING-*"));
    }

    [Fact]
    public void CreateTree_ThrowsBeforeWritingWhenDestinationFileExists()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "EXISTANT");
        File.WriteAllText(target, "keep");

        Assert.Throws<FolderConflictException>(() => _service.CreateTree(target, FolderTemplates.Model));

        Assert.Equal("keep", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(_root, ".MS3DPRINT-STAGING-*"));
    }

    [Fact]
    public void CreateTree_DoesNotUseOrDeleteAnExistingStagingDirectory()
    {
        var knownId = Guid.NewGuid();
        var staging = Path.Combine(_root, ".MS3DPRINT-STAGING-" + knownId.ToString("N"));
        Directory.CreateDirectory(staging);
        var marker = Path.Combine(staging, "existing.txt");
        File.WriteAllText(marker, "keep");
        var target = Path.Combine(_root, "NEW");
        var service = new FolderTreeService(() => knownId);

        Assert.Throws<FolderConflictException>(() => service.CreateTree(target, FolderTemplates.Model));

        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void CreateTree_CreatesCompleteProjectTemplate()
    {
        var target = Path.Combine(_root, "PROJET");

        _service.CreateTree(target, FolderTemplates.Project);

        Assert.All(new[]
        {
            "00_BRIEF_CLIENT", "01_DEVIS_FACTURES", "02_FICHIERS_CLIENT",
            "03_CAO_3D/01_MASTER", "03_CAO_3D/02_STEP", "03_CAO_3D/03_STL",
            "04_IMPRESSION_3D/01_STL_PRODUCTION", "04_IMPRESSION_3D/02_3MF",
            "04_IMPRESSION_3D/03_PARAMETRES", "05_PRODUCTION", "06_PHOTOS_RENDUS",
            "07_LIVRAISON", "99_ARCHIVES"
        }, folder => Assert.True(Directory.Exists(Path.Combine(target, folder)), folder));
        Assert.Equal(13, FolderTemplates.Project.Count);
        Assert.Empty(Directory.GetDirectories(_root, ".MS3DPRINT-STAGING-*"));
    }

    [Fact]
    public void CreateTree_CreatesOtherApprovedTemplates()
    {
        AssertTemplate("CLIENT", FolderTemplates.Client, "00_CLIENT", "99_ARCHIVES");
        AssertTemplate("MODEL", FolderTemplates.Model, "01_REFERENCES", "02_SOURCE", "03_CAO_MASTER", "04_STEP", "05_STL", "06_3MF", "07_RENDUS", "08_PHOTOS", "99_ARCHIVES");
        AssertTemplate("PRODUCT", FolderTemplates.Product, "01_CONCEPT_REFERENCES", "02_CAO", "03_STL", "04_3MF", "05_TESTS_PROTOTYPES", "06_PHOTOS_RENDUS", "07_MARKETING", "08_PRIX_COUTS", "99_ARCHIVES");
        AssertTemplate("SUPPLIER", FolderTemplates.Supplier, "01_CONTACT", "02_TARIFS", "03_COMMANDES", "04_FACTURES", "05_DOCUMENTATION", "99_ARCHIVES");
    }

    private void AssertTemplate(string destinationName, IReadOnlyList<string> template, params string[] expected)
    {
        var target = Path.Combine(_root, destinationName);
        _service.CreateTree(target, template);
        Assert.Equal(expected.Length, template.Count);
        Assert.All(expected, folder => Assert.True(Directory.Exists(Path.Combine(target, folder)), folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

using System.Collections;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateCommitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-commit-" + Guid.NewGuid().ToString("N"));
    private EstimateStore Store => new(_root);
    private string DirectoryPath => Path.Combine(_root, ".ms3dprint-manager", "estimates");
    private static EstimateProfile Quote() => new(Guid.NewGuid(), "Devis", "PLA", "Machine", new(), new(1, 2, 3, 4), DateTimeOffset.UtcNow);
    private static EstimateProfile Broken(EstimateProfile quote) => quote with
    { Inputs = quote.Inputs with { V2 = new(ExtraCosts: new BrokenCosts()) } };

    [Fact]
    public void Failed_creation_leaves_no_partial_quote_and_allows_retry()
    {
        var quote = Quote();
        Assert.Throws<IOException>(() => Store.Create(Broken(quote)));
        Assert.Empty(Directory.GetFiles(DirectoryPath));
        Store.Create(quote);
        Assert.Equal(quote, Assert.Single(Store.LoadAll()));
    }

    [Fact]
    public void Failed_update_preserves_the_previous_file_and_cleans_temporary_files()
    {
        var quote = Quote(); Store.Create(quote);
        var path = Path.Combine(DirectoryPath, quote.Id + ".json");
        var before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => Store.Update(Broken(quote)));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(DirectoryPath));
    }

    [Fact]
    public void Creating_an_existing_id_never_overwrites_the_quote()
    {
        var quote = Quote(); Store.Create(quote);
        Assert.Throws<IOException>(() => Store.Create(quote with { Name = "Écrasement" }));
        Assert.Equal(quote, Assert.Single(Store.LoadAll()));
        Assert.Single(Directory.GetFiles(DirectoryPath));
    }

    [Fact]
    public void Stale_editor_cannot_overwrite_a_more_recent_save()
    {
        var quote = Quote(); Store.Create(quote);
        var firstEditor = Assert.Single(Store.LoadAll());
        var secondEditor = Assert.Single(Store.LoadAll());
        Store.Update(firstEditor with { Name = "Version récente" });
        Assert.Throws<ProfileConflictException>(() => Store.Update(secondEditor with { Name = "Version périmée" }));
        Assert.Equal("Version récente", Assert.Single(Store.LoadAll()).Name);
        var reloaded = Assert.Single(Store.LoadAll());
        Store.Update(reloaded with { Name = "Après rechargement" });
        Assert.Equal("Après rechargement", Assert.Single(Store.LoadAll()).Name);
    }

    private sealed class BrokenCosts : IReadOnlyList<EstimateCostItem>
    {
        public int Count => 1;
        public EstimateCostItem this[int index] => throw new IOException("Échec simulé pendant la sérialisation.");
        public IEnumerator<EstimateCostItem> GetEnumerator() => throw new IOException("Échec simulé pendant la sérialisation.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

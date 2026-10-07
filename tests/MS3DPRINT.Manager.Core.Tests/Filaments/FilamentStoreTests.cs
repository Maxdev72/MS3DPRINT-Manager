using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Filaments;

public sealed class FilamentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-filaments-" + Guid.NewGuid().ToString("N"));
    private FilamentProfile Profile() => new(Guid.NewGuid(), "Bambu", "Basic", "PLA", 19.90m, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public void CreateUpdate_PersistsPriceAndStableIdentity()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        store.Update(profile with { PricePerKg = 24.90m, Brand = "Prusa", CreatedAt = profile.CreatedAt.AddDays(1) });
        var saved = Assert.Single(new FilamentStore(_root).LoadAll());
        Assert.Equal(profile.Id, saved.Id);
        Assert.Equal(profile.CreatedAt, saved.CreatedAt);
        Assert.Equal(24.90m, saved.PricePerKg);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".ms3dprint-manager", "filaments"), "*.tmp"));
    }

    [Fact]
    public void Duplicate_CreatesNewIdentityWithoutChangingOriginal()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        var copy = store.Duplicate(profile.Id);
        Assert.NotEqual(profile.Id, copy.Id);
        Assert.Equal(profile.PricePerKg, copy.PricePerKg);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Theory]
    [InlineData("", "Basic", "PLA", 0)]
    [InlineData("Brand", " ", "PLA", 0)]
    [InlineData("Brand", "Basic", "", 0)]
    [InlineData("Brand", "Basic", "PLA", -1)]
    public void InvalidProfile_DoesNotCreateMetadata(string brand, string name, string material, int price)
    {
        var store = new FilamentStore(_root);
        Assert.Throws<ArgumentException>(() => store.Create(Profile() with { Brand = brand, Name = name, Material = material, PricePerKg = price }));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void CollisionAndMissingUpdate_PreserveData()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        Assert.Throws<InvalidOperationException>(() => store.Create(profile with { PricePerKg = 90 }));
        Assert.Throws<InvalidOperationException>(() => store.Update(Profile()));
        Assert.Equal(profile.PricePerKg, Assert.Single(store.LoadAll()).PricePerKg);
    }

    [Fact]
    public void TrashRestore_RestoresIdenticalProfile()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        var entry = store.Trash(profile.Id);
        Assert.Empty(store.LoadAll());
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal(profile, Assert.Single(store.LoadAll()));
    }

    [Fact]
    public void LockedUpdate_PreservesOriginalAndCleansTemporaryFile()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        var directory = Path.Combine(_root, ".ms3dprint-manager", "filaments");
        using (File.Open(Path.Combine(directory, profile.Id + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => store.Update(profile with { PricePerKg = 99 }));
        Assert.Equal(profile.PricePerKg, Assert.Single(store.LoadAll()).PricePerKg);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void CorruptMetadata_IsReportedInsteadOfSilentlyLosingProfiles()
    {
        var store = new FilamentStore(_root);
        var profile = Profile();
        store.Create(profile);
        var path = Path.Combine(_root, ".ms3dprint-manager", "filaments", profile.Id + ".json");
        File.WriteAllText(path, "broken json");
        Assert.Throws<System.Text.Json.JsonException>(() => store.LoadAll());
        Assert.Equal("broken json", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("24,90", true, "24.90")]
    [InlineData("0", true, "0")]
    [InlineData("-1", false, "0")]
    [InlineData("2 4,90", false, "0")]
    [InlineData("abc", false, "0")]
    public void FrenchPrice_UsesDecimalCommaAndRejectsMalformedInput(string input, bool valid, string expected)
    {
        Assert.Equal(valid, FilamentPrice.TryParse(input, out var price));
        if (valid) Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), price);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

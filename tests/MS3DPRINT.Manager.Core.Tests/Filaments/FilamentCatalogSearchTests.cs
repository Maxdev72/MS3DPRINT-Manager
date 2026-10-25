using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Core.Tests.Filaments;

public sealed class FilamentCatalogSearchTests
{
    [Theory]
    [InlineData("PLA Silk", "PLA")]
    [InlineData("petg-cf", "PETG")]
    [InlineData("PC-ABS", "PC")]
    [InlineData("PET-CF", "PET")]
    [InlineData("PA6-GF", "Polyamides (PA)")]
    [InlineData("PA12 (Nylon)", "Polyamides (PA)")]
    [InlineData("PA612-ESD", "Polyamides (PA)")]
    [InlineData("CoPA", "Polyamides (PA)")]
    [InlineData("TPU 95A", "TPU")]
    [InlineData("Paille personnalisée", "Autres")]
    public void Families_include_legacy_names_and_do_not_confuse_custom_names(string material, string expected)
        => Assert.Equal(expected, FilamentCatalogSearch.Family(material));

    [Fact]
    public void Search_ignores_accents_combines_terms_and_never_changes_prices_or_abrasiveness()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = new FilamentProfile(Guid.NewGuid(), "Créations", "Série technique", "PA6-GF", 65.1234m, false, now, now, "Bleu");
        var filtered = FilamentCatalogSearch.Filter([reference], "creations BLEU verre", "Polyamides (PA)");
        Assert.Same(reference, Assert.Single(filtered));
        Assert.Equal(65.1234m, filtered[0].PricePerKg); Assert.False(filtered[0].IsAbrasive);
        Assert.Empty(FilamentCatalogSearch.Filter([reference], "rouge", FilamentCatalogSearch.AllFamilies));
        Assert.Empty(FilamentCatalogSearch.Filter([reference], "bleu", "PLA"));
        Assert.Equal(new[] { "GF" }, FilamentCatalogSearch.Variants(reference.Material));
    }
}

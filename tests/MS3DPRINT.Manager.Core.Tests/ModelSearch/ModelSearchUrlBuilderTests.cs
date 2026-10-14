using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Core.Tests.ModelSearch;

public sealed class ModelSearchUrlBuilderTests
{
    private static readonly ModelSearchSite Printables = new("Printables", "https://www.printables.com/search/models?q={q}");

    [Fact]
    public void Build_encodes_spaces_accents_and_reserved_characters()
    {
        var uri = ModelSearchUrlBuilder.Build(Printables, "spatule à mastic & colle #1");
        Assert.Equal("https://www.printables.com/search/models?q=spatule%20%C3%A0%20mastic%20%26%20colle%20%231", uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_returns_null_for_an_empty_query(string query) => Assert.Null(ModelSearchUrlBuilder.Build(Printables, query));

    [Fact]
    public void Build_returns_null_when_the_template_has_no_placeholder() =>
        Assert.Null(ModelSearchUrlBuilder.Build(new ModelSearchSite("X", "https://example.com/search"), "clip"));

    [Fact]
    public void Build_rejects_non_web_schemes() =>
        Assert.Null(ModelSearchUrlBuilder.Build(new ModelSearchSite("X", "file:///c:/{q}"), "clip"));

    [Fact]
    public void Defaults_all_contain_the_placeholder_and_build() =>
        Assert.All(ModelSearchSite.Defaults, site => Assert.NotNull(ModelSearchUrlBuilder.Build(site, "spatule mastic")));

    [Theory]
    [InlineData("https://makerworld.com/fr", "https://makerworld.com/fr")]
    [InlineData("printables.com", "https://printables.com/")]
    public void FromAddressBar_keeps_urls(string text, string expected) =>
        Assert.StartsWith(expected, ModelSearchUrlBuilder.FromAddressBar(text)!.AbsoluteUri);

    [Fact]
    public void FromAddressBar_searches_free_text() =>
        Assert.Equal("https://www.yeggi.com/q/spatule%20mastic/", ModelSearchUrlBuilder.FromAddressBar("spatule mastic")!.AbsoluteUri);

    [Fact]
    public void Defaults_are_twelve_distinct_sites_each_with_a_category_and_a_note()
    {
        Assert.Equal(12, ModelSearchSite.Defaults.Count);
        Assert.Equal(12, ModelSearchSite.Defaults.Select(site => site.Name).Distinct().Count());
        Assert.All(ModelSearchSite.Defaults, site =>
        {
            Assert.False(string.IsNullOrWhiteSpace(site.Category));
            Assert.False(string.IsNullOrWhiteSpace(site.Note));
        });
        Assert.Equal(3, ModelSearchSite.Defaults.Select(site => site.Category).Distinct().Count());
    }
}

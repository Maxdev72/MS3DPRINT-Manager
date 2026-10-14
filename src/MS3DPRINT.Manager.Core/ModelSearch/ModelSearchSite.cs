namespace MS3DPRINT.Manager.Core.ModelSearch;

/// <summary>A 3D-model website with the address template of its search page; <c>{q}</c> stands for the query.</summary>
public sealed record ModelSearchSite(string Name, string SearchUrlTemplate, bool Enabled = true)
{
    public const string QueryPlaceholder = "{q}";

    public static IReadOnlyList<ModelSearchSite> Defaults { get; } =
    [
        new("Yeggi", "https://www.yeggi.com/q/{q}/"),
        new("MakerWorld", "https://makerworld.com/fr/search/models?keyword={q}"),
        new("Printables", "https://www.printables.com/search/models?q={q}"),
        new("Thingiverse", "https://www.thingiverse.com/search?q={q}&type=things"),
        new("Thangs", "https://thangs.com/search/{q}?scope=all"),
        new("Cults3D", "https://cults3d.com/fr/recherche?q={q}"),
        new("MyMiniFactory", "https://www.myminifactory.com/search/?query={q}")
    ];
}

using System.Globalization;

namespace MS3DPRINT.Manager.Core.Filaments;

public static class FilamentCatalogSearch
{
    public const string AllFamilies = "Toutes les familles";
    public static IReadOnlyList<string> Families { get; } = Array.AsReadOnly(new[] { AllFamilies, "PLA", "PETG", "ABS", "ASA", "PC", "PET", "Polyamides (PA)", "PPS", "TPU", "Autres" });
    private static string[] Tokens(string value) => value.ToUpperInvariant().Split(['-', ' ', '+', '/', '(', ')', '_'], StringSplitOptions.RemoveEmptyEntries);

    public static string Family(string material)
    {
        var token = Tokens(material).FirstOrDefault() ?? "";
        if (token is "COPA" or "NYLON" or "POLYAMIDE" or "POLYAMIDES" or "PA" || token.Length > 2 && token.StartsWith("PA", StringComparison.Ordinal) && char.IsDigit(token[2])) return "Polyamides (PA)";
        return token is "PLA" or "PETG" or "ABS" or "ASA" or "PC" or "PET" or "PPS" or "TPU" ? token : "Autres";
    }

    public static IReadOnlyList<string> Variants(string material) => Tokens(material).Where(t => t is "CF" or "GF" or "ESD").Distinct().ToArray();

    public static IReadOnlyList<FilamentProfile> Filter(IEnumerable<FilamentProfile> source, string? query, string? family)
    {
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var compare = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
        return source.Where(f =>
        {
            var group = Family(f.Material);
            if (!string.IsNullOrEmpty(family) && family != AllFamilies && group != family) return false;
            var variants = Variants(f.Material);
            var text = $"{f.Brand} {f.Name} {f.Material} {f.Color} {group}"
                + (group == "Polyamides (PA)" ? " nylon" : "")
                + (variants.Contains("CF") ? " carbone" : "") + (variants.Contains("GF") ? " verre" : "")
                + (variants.Contains("ESD") ? " antistatique" : "") + (f.IsAbrasive ? " abrasif" : "");
            return terms.All(term => compare.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
        }).ToArray();
    }
}

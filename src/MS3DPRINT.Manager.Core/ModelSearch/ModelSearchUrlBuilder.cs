namespace MS3DPRINT.Manager.Core.ModelSearch;

public static class ModelSearchUrlBuilder
{
    /// <summary>The search address of <paramref name="site"/> for <paramref name="query"/>, or null when either is unusable.</summary>
    public static Uri? Build(ModelSearchSite site, string query)
    {
        ArgumentNullException.ThrowIfNull(site);
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0 || !site.SearchUrlTemplate.Contains(ModelSearchSite.QueryPlaceholder, StringComparison.Ordinal)) return null;
        var address = site.SearchUrlTemplate.Replace(ModelSearchSite.QueryPlaceholder, Uri.EscapeDataString(trimmed), StringComparison.Ordinal);
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
    }

    /// <summary>What was typed as a web address (a full URL, or a bare host such as <c>printables.com</c>); null for ordinary words.</summary>
    public static Uri? TryParseAddress(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http") return absolute;
        // a bare host needs a dot in its name: "file:///c:/x.txt" or "javascript:alert(1)" are not sites
        if (!trimmed.Contains(' ') && trimmed.Contains('.') && Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var guessed)
            && guessed.HostNameType == UriHostNameType.Dns && guessed.Host.Contains('.')) return guessed;
        return null;
    }

    /// <summary>Turns what a user typed in an address bar into an address: a URL as is, anything else as a web search.</summary>
    public static Uri? FromAddressBar(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;
        return TryParseAddress(trimmed) ?? Build(new ModelSearchSite("Yeggi", "https://www.yeggi.com/q/{q}/"), trimmed);
    }
}

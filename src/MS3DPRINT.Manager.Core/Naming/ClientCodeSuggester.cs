namespace MS3DPRINT.Manager.Core.Naming;

public static class ClientCodeSuggester
{
    /// <summary>First free code: the suggestion, then the suggestion followed by 2, 3, 4...</summary>
    public static string SuggestUnique(string clientName, IEnumerable<string> usedCodes)
    {
        ArgumentNullException.ThrowIfNull(usedCodes);
        var baseCode = Suggest(clientName);
        if (baseCode.Length == 0) return string.Empty;
        var used = usedCodes.Where(code => !string.IsNullOrWhiteSpace(code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseCode)) return baseCode;
        for (var number = 2; ; number++)
            if (!used.Contains(baseCode + number)) return baseCode + number;
    }

    public static string Suggest(string clientName)
    {
        var normalized = NameNormalizer.Normalize(clientName);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var words = normalized.Split('_');
        return words.Length > 1
            ? new string(words.Select(word => word[0]).ToArray())
            : normalized[..Math.Min(normalized.Length, 5)];
    }
}

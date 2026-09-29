namespace MS3DPRINT.Manager.Core.Naming;

public static class ClientCodeSuggester
{
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

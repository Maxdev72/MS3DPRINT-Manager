namespace MS3DPRINT.Manager.Core.Files;

/// <summary>Parses a print page range such as "1-3,5" into zero-based page indexes.</summary>
public static class PrintPageSelection
{
    public static IReadOnlyList<int> Parse(string? text, int pageCount)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Equals("toutes", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(0, pageCount).ToArray();

        var pages = new List<int>();
        foreach (var part in trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2 || !int.TryParse(bounds[0], out var first) || !int.TryParse(bounds[^1], out var last))
                throw new FormatException($"Plage de pages invalide : « {part} ». Exemple : 1-3,5.");
            if (first < 1 || last < first || last > pageCount)
                throw new FormatException($"Les pages doivent être comprises entre 1 et {pageCount} (« {part} »).");
            for (var page = first; page <= last; page++)
                if (!pages.Contains(page - 1)) pages.Add(page - 1);
        }
        if (pages.Count == 0) throw new FormatException("Aucune page à imprimer.");
        return pages;
    }
}

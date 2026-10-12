using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Display-only formatting of names. Folder names and imported company names are often upper case with underscores
/// ("GUY_LALLEMAND", "FELL AGENCY"); the screens show them as "Guy Lallemand" and "Fell Agency".
/// Stored names are never changed.
/// </summary>
public static class DisplayNames
{
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "du", "des", "la", "le", "les", "et", "en", "au", "aux", "un", "une", "sur", "sous", "pour", "d", "l"
    };

    public static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        // A name that is already written with lower case letters was typed by a person: keep it.
        if (!text.Contains('_') && text.Any(char.IsLower)) return text;

        var words = text.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
            words[index] = FormatWord(words[index], first: index == 0);
        return JoinElisions(words);
    }

    /// <summary>"Pions D Échecs" (from PIONS_D_ECHECS) becomes "Pions d’Échecs": a lone d or l before a vowel is an elision.</summary>
    private static string JoinElisions(string[] words)
    {
        var parts = new List<string>();
        for (var index = 0; index < words.Length; index++)
        {
            if (index + 1 < words.Length && words[index] is "d" or "l" && StartsWithVowel(words[index + 1]) && words[index].Length == 1 && index > 0)
            {
                parts.Add(words[index] + "’" + words[index + 1]);
                index++;
            }
            else parts.Add(words[index]);
        }
        return string.Join(' ', parts);
    }

    private static bool StartsWithVowel(string word)
    {
        var first = char.ToLowerInvariant(word[0]);
        return "aeiouyhàâäéèêëîïôöùûüœ".Contains(first);
    }

    private static string FormatWord(string word, bool first)
    {
        if (word.Any(char.IsLower) || word.Any(char.IsDigit)) return word; // "EPW175S", "3D", "Crâne"
        if (!first && SmallWords.Contains(word)) return word.ToLowerInvariant();
        if (word.Length <= 3 && IsLikelyAcronym(word)) return word; // MPO, MV; but not GUY or LEA, which are first names
        // folder names lost their accents: put back those of the words we know (EPEE → Épée)
        var lower = word.ToLowerInvariant();
        lower = FrenchAccents.TryRestore(lower) ?? lower;
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    /// <summary>Short words without a vowel in their first two letters read as acronyms (MV, MPO); the others as names (GUY, LEA).</summary>
    private static bool IsLikelyAcronym(string word)
        => !word.Take(2).Any(letter => "AEIOUY".Contains(letter));

    public static string Title(this ClientSummary client) => Format(client.DisplayName);
    public static string Title(this ProjectSummary project) => Format(project.ProjectName);
    public static string ClientTitle(this ProjectSummary project) => Format(project.ClientFolderName);
}

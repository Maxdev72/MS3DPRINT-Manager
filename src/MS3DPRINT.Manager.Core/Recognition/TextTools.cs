using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>One amount found in the text, with the words that surround it.</summary>
internal sealed record MoneyToken(decimal Value, int Line, int Index, string Raw, string Label);

/// <summary>Tolerant text helpers: OCR output is noisy, so everything is matched on a folded copy (no accents, lower case, spaces collapsed).</summary>
internal static partial class TextTools
{
    [GeneratedRegex(@"(?<![\p{L}\d.,/])(?<![\p{L}\d]-)(?:\d{1,3}(?:[ .,]\d{3})+|\d+)[.,]\d{2}(?!\d)(?![.,/]\d)(?!\s?%)", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"(?<![\d.,])(\d{1,6})\s?(?:€|eur\b|euros?\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WholeEuroRegex();

    [GeneratedRegex(@"(\d)\s?€\s?(\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex EuroSeparatorRegex();

    [GeneratedRegex(@"(?<=[\dOoIl|SBeêQ])\s*([.,])\s+(?=[\dOoIl|SBeêQ]{2}(?![\p{L}\d]))", RegexOptions.CultureInvariant)]
    private static partial Regex SplitDecimalRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ManySpacesRegex();

    /// <summary>Lower case, no accents, typographic quotes and dashes flattened: the form every keyword is searched in.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(c switch { '’' or '‘' or '`' or '´' => '\'', '–' or '—' or '‑' => '-', ' ' or ' ' or ' ' => ' ', 'œ' => 'o', 'Œ' => 'o', _ => char.ToLowerInvariant(c) });
        }
        return builder.ToString();
    }

    /// <summary>Splits the text into trimmed, space-collapsed non-empty lines and repairs the usual OCR damage inside numbers.</summary>
    public static List<string> Lines(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ').Replace('\t', ' ').Trim();
            if (line.Length == 0) continue;
            line = RepairDigitRuns(line);
            line = SplitDecimalRegex().Replace(line, "$1");
            line = RepairNumbers(line);
            line = EuroSeparatorRegex().Replace(line, "$1,$2 €");
            lines.Add(ManySpacesRegex().Replace(line, "  "));
        }
        return lines;
    }

    [GeneratedRegex(@"(?<![\p{L}])[\dOoIl|SBeêQ][\dOoIl|SBeêQ .,]*[.,][\dOoIl|SBeêQ]{2}(?![\p{L}\d])", RegexOptions.CultureInvariant)]
    private static partial Regex NoisyNumberRegex();

    /// <summary>"l2,5O" read for "12,50": letters that look like digits are turned back only inside tokens that are mostly digits.</summary>
    private static string RepairNumbers(string line) => NoisyNumberRegex().Replace(line, match =>
    {
        var token = match.Value;
        var digits = token.Count(char.IsDigit);
        var lookalikes = token.Count(IsLookalike);
        if (digits < 2 || lookalikes == 0 || lookalikes > digits) return token;
        var chars = token.Select(ToDigit);
        return new string(chars.ToArray());
    });

    private static bool IsLookalike(char c) => c is 'O' or 'o' or 'I' or 'l' or '|' or 'S' or 'B' or 'e' or 'ê' or 'Q';
    private static char ToDigit(char c) => c switch { 'O' or 'o' or 'e' or 'ê' or 'Q' => '0', 'I' or 'l' or '|' => '1', 'S' => '5', 'B' => '8', _ => c };

    [GeneratedRegex(@"(?<![\p{L}\d])[\dOoIl|SBeêQ]{1,4}(?:[/.\-][\dOoIl|SBeêQ]{1,4}){1,2}(?![\p{L}\d])|(?<![\p{L}\d])[\dOoIl|SBeêQ]{3,}(?![\p{L}\d])", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRunRegex();

    /// <summary>Dates and long numbers ("24/1e/2e26", "ee4512") with letters read in place of digits: repaired when most of the token is digits.</summary>
    private static string RepairDigitRuns(string line) => DigitRunRegex().Replace(line, match =>
    {
        var token = match.Value;
        var digits = token.Count(char.IsDigit);
        var lookalikes = token.Count(IsLookalike);
        if (lookalikes == 0 || digits < 2 && !token.Contains('/') || lookalikes > digits + 2) return token;
        return new string(token.Select(ToDigit).ToArray());
    });

    public static bool TryParseMoney(string token, out decimal value)
    {
        value = 0;
        var text = token.Trim();
        var separator = Math.Max(text.LastIndexOf(','), text.LastIndexOf('.'));
        if (separator < 0 || text.Length - separator - 1 != 2) return false;
        var whole = new string(text[..separator].Where(char.IsDigit).ToArray());
        if (whole.Length == 0) whole = "0";
        return decimal.TryParse(whole + "." + text[(separator + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value is >= 0 and < 10_000_000;
    }

    /// <summary>Every amount of every line. A line made only of the amount takes the line above as its label (column layouts).</summary>
    public static List<MoneyToken> Amounts(IReadOnlyList<string> lines)
    {
        var result = new List<MoneyToken>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var found = new List<(int Index, string Raw, decimal Value)>();
            foreach (Match m in MoneyRegex().Matches(line)) if (TryParseMoney(m.Value, out var v)) found.Add((m.Index, m.Value, v));
            foreach (Match m in WholeEuroRegex().Matches(line))
                if (!found.Any(f => m.Index < f.Index + f.Raw.Length && f.Index < m.Index + m.Length) && decimal.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0)
                    found.Add((m.Index, m.Value, v));
            foreach (var (index, raw, value) in found.OrderBy(f => f.Index))
            {
                var label = (line[..index] + " " + line[(index + raw.Length)..]).Trim();
                if (!label.Any(char.IsLetter) && i > 0) label = lines[i - 1] + " " + label;
                result.Add(new MoneyToken(value, i, index, raw, label));
            }
        }
        return result;
    }

    /// <summary>"Total HT / TVA / Total TTC" printed first and "10,00 / 2,00 / 12,00" printed after: the labels and the amounts are put back side by side.</summary>
    public static List<string> ZipColumns(List<string> lines)
    {
        var result = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            if (!HasAmount(lines[i]) && LooksLikeTotalLabel(lines[i]))
            {
                var run = i; var labels = new List<string>();
                while (run < lines.Count && !HasAmount(lines[run]) && labels.Count < 8 && (LooksLikeTotalLabel(lines[run]) || labels.Count > 0 && lines[run].Length < 40)) { labels.Add(lines[run]); run++; }
                var amounts = new List<string>();
                while (run + amounts.Count < lines.Count && amounts.Count < labels.Count && IsAmountOnly(lines[run + amounts.Count])) amounts.Add(lines[run + amounts.Count]);
                if (labels.Count >= 2 && amounts.Count == labels.Count)
                {
                    for (var k = 0; k < labels.Count; k++) result.Add(labels[k] + "  " + amounts[k]);
                    i = run + amounts.Count; continue;
                }
            }
            result.Add(lines[i]); i++;
        }
        return result;
    }

    private static bool HasAmount(string line) => MoneyRegex().IsMatch(line) || WholeEuroRegex().IsMatch(line);
    private static bool IsAmountOnly(string line) => HasAmount(line) && !CurrencyWordRegex().Replace(Fold(line), "").Any(char.IsLetter);

    [GeneratedRegex(@"\b(?:eur|euros?|ttc|ht)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyWordRegex();
    private static bool LooksLikeTotalLabel(string line)
    {
        var f = Fold(line);
        return f.Contains("total") || f.Contains("tva") || f.Contains("net a payer") || f.Contains("montant") || f.Contains("a payer") || f.Contains(" ht") || f.StartsWith("ht");
    }

    /// <summary>Whole-word search in folded text: "action" is not found in "transaction".</summary>
    public static bool ContainsWord(string folded, string name)
    {
        var needle = Fold(name).Trim();
        if (needle.Length == 0) return false;
        for (var index = folded.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = folded.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            var before = index == 0 || !char.IsLetterOrDigit(folded[index - 1]);
            var end = index + needle.Length;
            var after = end >= folded.Length || !char.IsLetterOrDigit(folded[end]);
            if (before && after) return true;
        }
        return false;
    }

    public static string Collapse(string text) => ManySpacesRegex().Replace(text, " ").Trim();

    public static string DigitsOnly(string text) => new(text.Where(char.IsDigit).ToArray());
}

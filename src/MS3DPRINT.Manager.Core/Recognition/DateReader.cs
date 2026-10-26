using System.Globalization;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>Finds the date of the document among all dates: the issue or purchase date, not the due date, the delivery date or a validity limit.</summary>
internal static partial class DateReader
{
    private static readonly string[] MonthStems = ["janv", "fev", "mars", "avr", "mai", "juin", "juil", "aout", "sept", "oct", "nov", "dec"];

    [GeneratedRegex(@"(?<![\d/.\-])(\d{1,2})\s?[/.\-]\s?(\d{1,2})\s?[/.\-]\s?(\d{4}|\d{2})(?![\d/])", RegexOptions.CultureInvariant)]
    private static partial Regex Numeric();

    [GeneratedRegex(@"(?<!\d)(\d{4})\s?-\s?(\d{2})\s?-\s?(\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Iso();

    [GeneratedRegex(@"(?<!\d)(\d{1,2})(?:er)?\s+(janvier|janv|fevrier|fev|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec)\.?,?\s+(\d{4}|\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Long();

    [GeneratedRegex(@"\b(january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sep|sept|oct|nov|dec)\.?\s+(\d{1,2})(?:st|nd|rd|th)?,?\s+(\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex English();

    [GeneratedRegex(@"date\s*(?:de\s*)?(?:facture|facturation|emission|d'emission|achat|d'achat|commande|de\s*la\s*commande|paiement|de\s*paiement|vente|operation)|facture\s*du|emise?\s*le|emis\s*le|mise\s*le|achat\s*du|commande\s*du|paye\s*le|ticket|\bdate\s*:|\ble\b\s*$|invoice\s*date|order\s*date|date\s*of\s*(?:issue|purchase)|issued|\bdate\b", RegexOptions.CultureInvariant)]
    private static partial Regex Good();

    [GeneratedRegex(@"echeance|date\s*limite|avant\s*le|valable|validite|jusqu|au\s*plus\s*tard|livraison|expedition|naissance|expire|periode|prevue?|retour|garantie|\bdu\s.*\bau\b|due\s*date|payable\s*(?:le|avant)|delivery|expiry|a\s*regler\s*avant|penalit|retard", RegexOptions.CultureInvariant)]
    private static partial Regex Bad();

    [GeneratedRegex(@"\d{1,2}\s?[h:]\s?\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex Time();

    public static ReadField<DateOnly>? Read(IReadOnlyList<string> lines, DateOnly today)
    {
        var found = new List<(DateOnly Date, double Score, string Evidence)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var folded = TextTools.Fold(line);
            foreach (var (date, index, length) in Matches(line, folded, today))
            {
                // The words just before the date matter most; the previous line is used when the date stands alone.
                var before = folded[..Math.Min(folded.Length, index)];
                var context = before.Any(char.IsLetter) ? before : (i > 0 ? TextTools.Fold(lines[i - 1]) + " " + before : before);
                var after = folded[Math.Min(folded.Length, index + length)..];
                var score = 0.0;
                if (Good().IsMatch(context)) score += 50;
                if (Bad().IsMatch(context)) score -= 70;
                if (Bad().IsMatch(folded) && !Good().IsMatch(context)) score -= 25;
                if (Time().IsMatch(after) || Time().IsMatch(before)) score += 15;
                score += 10.0 * (1.0 - (double)i / Math.Max(1, lines.Count));
                if (date > today.AddDays(1)) score -= 90;
                if (date < today.AddYears(-12)) score -= 90;
                found.Add((date, score, TextTools.Collapse(line.Length > 90 ? line[..90] + "…" : line)));
            }
        }
        if (found.Count == 0) return null;
        var best = found.OrderByDescending(f => f.Score).ThenBy(f => f.Date).First();
        var confidence = best.Score >= 45 ? 0.92 : best.Score >= 10 ? 0.78 : best.Score >= -20 ? 0.5 : 0.25;
        if (found.Select(f => f.Date).Distinct().Count() == 1) confidence = Math.Min(0.97, confidence + 0.05);
        return new(best.Date, confidence, best.Evidence);
    }

    private static IEnumerable<(DateOnly Date, int Index, int Length)> Matches(string line, string folded, DateOnly today)
    {
        foreach (Match m in Numeric().Matches(folded))
            if (Build(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), m.Groups[3].Length == 2, today) is { } d) yield return (d, m.Index, m.Length);
        foreach (Match m in Iso().Matches(folded))
            if (Build(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), false, today, strictDayFirst: true) is { } d) yield return (d, m.Index, m.Length);
        foreach (Match m in Long().Matches(folded))
        {
            var month = Array.FindIndex(MonthStems, stem => m.Groups[2].Value.StartsWith(stem, StringComparison.Ordinal)) + 1;
            if (month > 0 && Build(int.Parse(m.Groups[3].Value), month, int.Parse(m.Groups[1].Value), m.Groups[3].Length == 2, today, strictDayFirst: true) is { } d) yield return (d, m.Index, m.Length);
        }
        foreach (Match m in English().Matches(folded))
        {
            var name = m.Groups[1].Value[..3];
            var month = DateTimeFormatInfo.InvariantInfo.AbbreviatedMonthNames.ToList().FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) + 1;
            if (month > 0 && Build(int.Parse(m.Groups[3].Value), month, int.Parse(m.Groups[2].Value), false, today, strictDayFirst: true) is { } d) yield return (d, m.Index, m.Length);
        }
    }

    /// <summary>French documents are day first; a month number above 12 with a day number of 12 or less means the American order was used.</summary>
    private static DateOnly? Build(int year, int month, int day, bool shortYear, DateOnly today, bool strictDayFirst = false)
    {
        if (shortYear) year += year <= (today.Year % 100) + 1 ? 2000 : 1900;
        if (year < 1990 || year > 2100) return null;
        if (!strictDayFirst && month > 12 && day <= 12) (month, day) = (day, month);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }
}

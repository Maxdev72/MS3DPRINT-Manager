using System.Globalization;
using System.Text;

namespace MS3DPRINT.Manager.Core.Purchases;

/// <summary>Imports bank values verbatim from CSV or a tab-separated spreadsheet selection.</summary>
public static class ExpenseScheduleImport
{
    public static IReadOnlyList<ExpenseInstallment> Parse(string text, Guid planId)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500000) throw new ArgumentException("Collez un tableau non vide, limité à 500 Ko.");
        text = text.TrimStart('\uFEFF');
        var first = text.Split('\n')[0]; var separator = first.Contains('\t') ? '\t' : first.Contains(';') ? ';' : ',';
        var records = ReadRows(text, separator).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToArray();
        if (records.Length == 0 || records.Length > 1201) throw new ArgumentException("Le tableau doit contenir entre 1 et 1 200 échéances.");
        var names = records[0].Select(Normalize).ToArray();
        var hasHeader = !Date(records[0][0], out _);
        int Column(params string[] aliases) => Array.FindIndex(names, n => aliases.Contains(n));
        var dateColumn = hasHeader ? Column("date", "echeance", "dateecheance", "datedeprelevement") : 0;
        var amountColumn = hasHeader ? Column("montant", "mensualite", "montantecheance", "total", "montanttotal") : 1;
        if (dateColumn < 0 || amountColumn < 0) throw new ArgumentException("Le tableau doit avoir les colonnes Date et Montant (ou Mensualité).");
        var capital = hasHeader ? Column("capital", "capitalrembourse", "amortissement") : -1;
        var interest = hasHeader ? Column("interet", "interets") : -1;
        var insurance = hasHeader ? Column("assurance") : -1;
        var balance = hasHeader ? Column("capitalrestantdu", "solde", "capitalrestant") : -1;
        var result = new List<ExpenseInstallment>();
        for (var i = hasHeader ? 1 : 0; i < records.Length; i++)
        {
            var row = records[i];
            if (row.Length != (hasHeader ? names.Length : 2)) throw new ArgumentException($"Ligne {i + 1} : nombre de colonnes incorrect. Vérifiez le séparateur et les guillemets autour des montants.");
            string Cell(int n) => n >= 0 && n < row.Length ? row[n].Trim() : "";
            var line = i + 1;
            if (!Date(Cell(dateColumn), out var due)) throw new ArgumentException($"Ligne {line} : date invalide (JJ/MM/AAAA ou AAAA-MM-JJ).");
            if (!Amount(Cell(amountColumn), out var amount) || amount <= 0) throw new ArgumentException($"Ligne {line} : montant invalide ou non positif.");
            decimal? Optional(int column)
            {
                var value = Cell(column); if (value.Length == 0) return null;
                if (!Amount(value, out var number)) throw new ArgumentException($"Ligne {line} : valeur bancaire invalide « {value} ».");
                return number;
            }
            if (result.Count > 0 && due < result[^1].DueOn) throw new ArgumentException($"Ligne {line} : classez les échéances par date croissante.");
            result.Add(new(ExpensePlans.TermId(planId, result.Count, due), due, amount, Optional(capital), Optional(interest), Optional(insurance), Optional(balance)));
        }
        if (result.Count == 0) throw new ArgumentException("Le tableau ne contient aucune échéance.");
        return result;
    }

    public static bool Amount(string? text, out decimal amount)
    {
        var cleaned = (text ?? "").Replace("€", "").Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "");
        const NumberStyles style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        return (decimal.TryParse(cleaned, style, CultureInfo.GetCultureInfo("fr-FR"), out amount)
            || decimal.TryParse(cleaned, style, CultureInfo.InvariantCulture, out amount))
            && amount >= 0 && amount <= 1000000000m && amount == decimal.Round(amount, 2);
    }
    public static bool Date(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text?.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Normalize(string text)
        => new(text.Normalize(NormalizationForm.FormD).Where(c => char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<string[]> ReadRows(string text, char separator)
    {
        var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == separator && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((c == '\n' || c == '\r') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear(); yield return row.ToArray(); row.Clear();
            }
            else cell.Append(c);
        }
        if (quoted) throw new ArgumentException("Le tableau comporte des guillemets non fermés.");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); yield return row.ToArray(); }
    }
}

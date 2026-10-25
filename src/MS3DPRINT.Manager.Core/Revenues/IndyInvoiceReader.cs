using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>What could be read from an invoice PDF. Every value is a proposal that the user confirms; any of them may be missing.</summary>
public sealed record IndyInvoiceData(string? InvoiceNumber, DateOnly? IssuedOn, decimal? TotalTtc, string? ClientName, RevenueNature? Nature);

/// <summary>Local reading of invoices issued with Indy. Nothing leaves the computer and nothing is guessed silently.</summary>
public static class IndyInvoiceReader
{
    private const long MaxPdfBytes = 20 * 1024 * 1024;
    private static readonly Regex NumberPattern = new(@"\bFacture(?:\s+d\W{1,2}acompte|\s+de\s+solde|\s+d\W{1,2}avoir)?\s+([A-Z0-9][A-Z0-9_\-/]{3,})", RegexOptions.CultureInvariant);
    // Former invoices (Henrri): "FACTURE N° 25-10-31" and "Le samedi 25 octobre 2025".
    private static readonly Regex LegacyNumberPattern = new(@"FACTURE\s+N\W{0,3}\s*([0-9]{2}-[0-9]{2}-[0-9]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex LongDatePattern = new(@"\b(?:lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)?\s*(\d{1,2})(?:er)?\s+(janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[uû]t|septembre|octobre|novembre|d[ée]cembre)\s+(\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] MonthStems = ["janv", "f", "mars", "avr", "mai", "juin", "juil", "ao", "sept", "oct", "nov", "d"];
    // Invoices written with a spreadsheet-style template: "FAC-2026-006", "Date : 30/07/2026", only "TOTAL HT" (VAT not applicable).
    private static readonly Regex PlainNumberPattern = new(@"(?<![A-Z0-9])([A-Z]{2,6}-[0-9]{4}-[0-9]{2,6})(?![A-Z0-9])", RegexOptions.CultureInvariant);
    private static readonly Regex PlainDatePattern = new(@"\bDate\s*:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TotalHtPattern = new(@"Total\s+HT\s+([0-9][0-9\s\u00a0\u202f]*[.,][0-9]{2})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DatePattern = new(@"mise\s+le\s+(\d{2}/\d{2}/\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TotalPattern = new(@"Total\s+TTC\s+([0-9][0-9\s  ]*[.,][0-9]{2})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Reads the first pages of the PDF. Returns null when the file is not a readable PDF or holds none of the expected fields.</summary>
    public static IndyInvoiceData? Read(string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        var info = new FileInfo(pdfPath);
        if (!info.Exists || info.Length is 0 or > MaxPdfBytes) return null;
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            var text = new System.Text.StringBuilder();
            string? client = null;
            foreach (var page in document.GetPages().Take(3))
            {
                text.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
                if (page.Number == 1) client = ClientNameOnFirstPage(page);
            }
            return Parse(text.ToString(), client);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException
            or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        { return null; }
    }

    public static IndyInvoiceData? Parse(string text, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var number = NumberPattern.Match(text) is { Success: true } n ? n.Groups[1].Value
            : LegacyNumberPattern.Match(text) is { Success: true } legacy ? legacy.Groups[1].Value
            : PlainNumberPattern.Match(text) is { Success: true } plain && Regex.IsMatch(text, "FACTURE", RegexOptions.IgnoreCase) ? plain.Groups[1].Value : null;
        DateOnly? issued = DateOnly.TryParseExact(DatePattern.Match(text).Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
            : DateOnly.TryParseExact(PlainDatePattern.Match(text).Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var plainDate) ? plainDate : LongDate(text);
        decimal? total = null;
        var totals = TotalPattern.Matches(text);
        if (totals.Count == 0) totals = TotalHtPattern.Matches(text); // VAT not applicable: the total excluding tax is the amount due
        if (totals.Count > 0)
        {
            var raw = new string(totals[^1].Groups[1].Value.Where(c => char.IsDigit(c) || c is ',' or '.').ToArray()).Replace(',', '.');
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0) total = value;
        }
        var goods = text.Contains("vente de bien", StringComparison.OrdinalIgnoreCase) || text.Contains("vente de marchandise", StringComparison.OrdinalIgnoreCase);
        var services = text.Contains("prestation de service", StringComparison.OrdinalIgnoreCase);
        RevenueNature? nature = goods && services ? RevenueNature.Mixed : goods ? RevenueNature.Goods : services ? RevenueNature.Services : null;
        if (number is null && issued is null && total is null) return null;
        return new IndyInvoiceData(number, issued, total, string.IsNullOrWhiteSpace(clientName) ? null : clientName.Trim(), nature);
    }

    /// <summary>"D E S T I N A T A I R E" (letters spaced or not) heads the recipient block; its name is the next line below, same side.</summary>
    private static string? RecipientUnderMarker(UglyToad.PdfPig.Content.Page page, UglyToad.PdfPig.Content.Word[] words)
    {
        var lines = words.GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4)).OrderByDescending(g => g.Key).Select(g => g.OrderBy(w => w.BoundingBox.Left).ToArray()).ToArray();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var flat = string.Concat(lines[i].Select(w => w.Text)).Replace(" ", "").ToUpperInvariant();
            if (flat != "DESTINATAIRE" && flat != "CLIENT") continue;
            var left = lines[i].Min(w => w.BoundingBox.Left) - 8;
            var next = lines[i + 1].Where(w => w.BoundingBox.Left >= left).Select(w => w.Text);
            var name = string.Join(' ', next);
            return name.Length is > 0 and <= 120 ? name : null;
        }
        return null;
    }

    private static DateOnly? LongDate(string text)
    {
        var match = LongDatePattern.Match(text);
        if (!match.Success) return null;
        var name = match.Groups[2].Value.ToLowerInvariant();
        var month = Array.FindIndex(MonthStems, stem => name.StartsWith(stem, StringComparison.Ordinal)) + 1;
        if (month == 0 || !int.TryParse(match.Groups[1].Value, out var day) || !int.TryParse(match.Groups[3].Value, out var year)) return null;
        return day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
    }

    /// <summary>The recipient block sits in the right half above the "Facture …" title: its first line is the client name.</summary>
    private static string? ClientNameOnFirstPage(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToArray();
        if (RecipientUnderMarker(page, words) is { } marked) return marked;
        var title = words.FirstOrDefault(w => w.Text is "Facture" or "FACTURE");
        if (title is null) return null;
        var above = words.Where(w => w.BoundingBox.Bottom > title.BoundingBox.Top).ToArray();
        var right = above.Where(w => w.BoundingBox.Left > page.Width / 2).ToArray();
        if (right.Length == 0) return null;
        // The recipient's name shares its baseline with the issuer's name on the left; a lone banner above it (e.g. a title) is skipped.
        string? first = null;
        foreach (var bottom in right.Select(w => w.BoundingBox.Bottom).OrderByDescending(b => b).Distinct())
        {
            var rightLine = right.Where(w => Math.Abs(w.BoundingBox.Bottom - bottom) < 4).OrderBy(w => w.BoundingBox.Left).Select(w => w.Text);
            var name = string.Join(' ', rightLine);
            if (name.Length is 0 or > 120) continue;
            first ??= name;
            if (above.Any(w => w.BoundingBox.Left < page.Width / 2 && Math.Abs(w.BoundingBox.Bottom - bottom) < 4)) return name;
        }
        return first;
    }
}

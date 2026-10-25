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
    private static readonly Regex NumberPattern = new(@"\bFacture\s+([A-Z0-9][A-Z0-9_\-/]{3,})", RegexOptions.CultureInvariant);
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
        var number = NumberPattern.Match(text) is { Success: true } n ? n.Groups[1].Value : null;
        DateOnly? issued = DateOnly.TryParseExact(DatePattern.Match(text).Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        decimal? total = null;
        var totals = TotalPattern.Matches(text);
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

    /// <summary>The recipient block sits in the right half above the "Facture …" title: its first line is the client name.</summary>
    private static string? ClientNameOnFirstPage(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToArray();
        var title = words.FirstOrDefault(w => w.Text == "Facture");
        if (title is null) return null;
        var right = words.Where(w => w.BoundingBox.Left > page.Width / 2 && w.BoundingBox.Bottom > title.BoundingBox.Top).ToArray();
        if (right.Length == 0) return null;
        var top = right.Max(w => w.BoundingBox.Bottom);
        var line = right.Where(w => Math.Abs(w.BoundingBox.Bottom - top) < 4).OrderBy(w => w.BoundingBox.Left).Select(w => w.Text);
        var name = string.Join(' ', line);
        return name.Length is > 0 and <= 120 ? name : null;
    }
}

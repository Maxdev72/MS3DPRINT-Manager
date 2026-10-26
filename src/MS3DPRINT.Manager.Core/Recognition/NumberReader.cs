using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>Finds the number that identifies the document: invoice number first, then order, then ticket number.</summary>
internal static partial class NumberReader
{
    private const string Token = @"([A-Z0-9][A-Z0-9\-/_.]{2,28})";
    private static readonly string[] NotNumbers = ["DU", "DE", "LE", "LA", "NO", "NUM", "DATE", "TOTAL", "CLIENT", "FACTURE", "INVOICE", "PAGE"];

    [GeneratedRegex(@"(?:n(?:°|o|º|°|um(?:ero)?)\.?\s*(?:de\s*)?(?:facture|invoice|fact\.?)|(?:facture|invoice|fact\.?|avoir)\s*(?:d'acompte\s*|de\s*solde\s*)?(?:n(?:°|o|º|um(?:ero)?)\.?|#)|(?:facture|invoice|fact\.?)\s*(?:number|no\.?)|(?:facture|invoice)\s*[:\-])\s*[:\-]?\s*" + Token, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumber();

    [GeneratedRegex(@"\b(?:facture|invoice)\s+" + Token, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceWord();

    [GeneratedRegex(@"(?:n(?:°|o|º|um(?:ero)?)\.?\s*(?:de\s*)?(?:commande|order)|(?:commande|order)\s*(?:n(?:°|o|º|um(?:ero)?)\.?|#|number|no\.?)?)\s*[:\-]?\s*" + Token, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OrderNumber();

    [GeneratedRegex(@"(?:ticket|re[cç]u|bon|caisse|transaction|operation|recu)\s*(?:de\s*caisse\s*)?(?:n(?:°|o|º|um(?:ero)?)\.?|#)?\s*[:\-]?\s*" + Token, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TicketNumber();

    [GeneratedRegex(@"\b([A-Z]{2,6}-?\d{4,8}(?:-\d{1,6})?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Plain();

    public static ReadField<string>? Read(IReadOnlyList<string> lines, DocumentKind kind)
    {
        ReadField<string>? Try(Regex regex, double confidence, string why)
        {
            foreach (var line in lines)
                foreach (Match match in regex.Matches(line))
                    if (Valid(match.Groups[1].Value) is { } number) return new(number, confidence, TextTools.Collapse(line.Length > 90 ? line[..90] + "…" : line));
            return null;
        }
        var ordered = kind == DocumentKind.Receipt
            ? new[] { (TicketNumber(), 0.7, "ticket"), (InvoiceNumber(), 0.9, "facture"), (OrderNumber(), 0.7, "commande") }
            : new[] { (InvoiceNumber(), 0.92, "facture"), (InvoiceWord(), 0.8, "facture"), (OrderNumber(), 0.75, "commande"), (TicketNumber(), 0.6, "ticket") };
        foreach (var (regex, confidence, why) in ordered)
            if (Try(regex, confidence, why) is { } found) return found;
        if (kind == DocumentKind.Invoice)
            foreach (var line in lines.Take(25))
                if (Plain().Match(line) is { Success: true } plain && Valid(plain.Groups[1].Value) is { } number && !line.Contains("SIRET", StringComparison.OrdinalIgnoreCase) && !line.Contains("TVA", StringComparison.OrdinalIgnoreCase))
                    return new(number, 0.5, TextTools.Collapse(line));
        return null;
    }

    private static string? Valid(string raw)
    {
        var token = raw.Trim().TrimEnd('.', '-', '/', '_', ':', ',');
        if (token.Length < 3 || !token.Any(char.IsDigit)) return null;
        if (NotNumbers.Contains(token, StringComparer.OrdinalIgnoreCase)) return null;
        if (Regex.IsMatch(token, @"^\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}$")) return null; // a date
        if (Regex.IsMatch(token, @"^\d{1,3}[.,]\d{2}$")) return null; // an amount
        return token;
    }
}

using System.Globalization;
using System.Text;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Purchases;

/// <summary>Exports of one year of the purchase register: CSV and printable PDF. The workshop header is shared with the revenue book.</summary>
public static class PurchaseExporter
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] MonthNames = ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    public static string ToCsv(PurchaseYear year)
    {
        ArgumentNullException.ThrowIfNull(year);
        var builder = new StringBuilder();
        builder.AppendLine("Date d’achat;Fournisseur;Libellé;Montant;Mode de règlement;Pièce justificative;Projet;Statut;Motif d’annulation");
        foreach (var line in year.Lines)
        {
            var e = line.Entry;
            builder.AppendJoin(';', e.PurchasedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Text(e.Supplier), Text(e.Label), e.Amount.ToString("0.00", French),
                Text(e.PaymentMethod), Text(e.Reference), Text(e.ProjectRef), line.IsCancelled ? "Annulé" : "Valide", Text(line.Cancellation?.Reason)).AppendLine();
        }
        return builder.ToString();
    }

    public static byte[] ToPdf(PurchaseYear year, RevenueHeader header)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(header);
        var summary = PurchaseTotals.Compute(year.Lines);
        var identity = string.Join(" — ", new[] { header.BusinessName, header.OwnerName, string.IsNullOrWhiteSpace(header.Siret) ? "" : "SIRET " + header.Siret }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var subtitle = new List<string>
        {
            identity.Length > 0 ? identity : "Atelier non renseigné (Réglages)",
            $"Édité le {DateTime.Today:dd/MM/yyyy}. Les achats annulés restent visibles, barrés, et sont exclus des totaux."
        };
        double[] x = [0, 52, 170, 330, 405, 480, 575, 690];
        int[] chars = [10, 25, 33, 13, 14, 20, 15, 17];
        var rows = new List<PdfLine>();
        foreach (var line in year.Lines)
        {
            var e = line.Entry;
            rows.Add(new PdfLine([e.PurchasedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), e.Supplier, e.Label, RevenueExporter.Money(e.Amount), e.PaymentMethod,
                e.Reference, e.ProjectRef ?? "", line.IsCancelled ? "Annulé : " + line.Cancellation!.Reason : "Valide"], Struck: line.IsCancelled));
        }
        rows.Add(new PdfLine([]));
        rows.Add(new PdfLine(["", "TOTAUX PAR MOIS", "", "Total"], Bold: true));
        for (var month = 0; month < 12; month++) rows.Add(new PdfLine(["", MonthNames[month], "", RevenueExporter.Money(summary.Months[month])]));
        rows.Add(new PdfLine(["", $"TOTAL {year.Year}", "", RevenueExporter.Money(summary.Total)], Bold: true));
        return SimplePdfWriter.Write($"Registre des achats {year.Year}", subtitle,
            new PdfLine(["Date", "Fournisseur", "Libellé", "Montant", "Règlement", "Pièce", "Projet", "Statut"], Bold: true), rows, x, chars);
    }

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = value.Replace("\r", " ").Replace("\n", " ");
        if ("=+-@\t".Contains(clean[0])) clean = "'" + clean;
        return clean.Contains(';') || clean.Contains('"') ? "\"" + clean.Replace("\"", "\"\"") + "\"" : clean;
    }
}

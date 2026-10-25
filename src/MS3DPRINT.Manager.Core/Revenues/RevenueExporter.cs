using System.Globalization;
using System.Text;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>Workshop identity printed at the top of the book.</summary>
public sealed record RevenueHeader(string BusinessName = "", string Siret = "", string OwnerName = "", string Address = "", string Email = "", string Phone = "")
{
    /// <summary>Second printed line: postal address, e-mail and phone when they are filled in; null otherwise.</summary>
    public string? ContactLine()
    {
        var line = string.Join(" — ", new[] { Address, Email, Phone }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
        return line.Length == 0 ? null : line;
    }
}

public sealed class RevenueHeaderStore
{
    private readonly string _path;
    public RevenueHeaderStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "revenue-header.json");
    }

    public RevenueHeader Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return new();
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<RevenueHeader>(stream) ?? new();
    }

    public void Save(RevenueHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var mutation = ProfileMutationLock.Acquire(_path);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, header, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>Exports of one year of the revenue book: CSV, printable PDF and the totals used for the turnover declaration.</summary>
public static class RevenueExporter
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] MonthNames = ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    public static string Money(decimal amount) => amount.ToString("N2", French).Replace(' ', ' ').Replace(' ', ' ') + " €";

    public static string NatureName(RevenueNature nature) => nature switch
    {
        RevenueNature.Goods => "Marchandises",
        RevenueNature.Services => "Prestations",
        _ => "Mixte",
    };

    /// <summary>Semicolon-separated, comma decimals, UTF-8; cells that a spreadsheet could read as a formula are neutralised.</summary>
    public static string ToCsv(RevenueYear year)
    {
        ArgumentNullException.ThrowIfNull(year);
        var builder = new StringBuilder();
        builder.AppendLine("Date d’encaissement;Client;Libellé;Nature;Marchandises;Prestations;Montant;Mode de règlement;Pièce justificative;Projet;Statut;Motif d’annulation");
        foreach (var line in year.Lines)
        {
            var e = line.Entry;
            builder.AppendJoin(';',
                e.ReceivedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Text(e.ClientName), Text(e.Label), NatureName(e.Nature),
                Number(e.GoodsAmount), Number(e.ServicesAmount), Number(e.Amount), Text(e.PaymentMethod), Text(e.InvoiceNumber), Text(e.ProjectRef),
                line.IsCancelled ? "Annulée" : "Valide", Text(line.Cancellation?.Reason)).AppendLine();
        }
        return builder.ToString();
    }

    public static string DeclarationSummary(int year, RevenueSummary summary, RevenueHeader header)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(header);
        var builder = new StringBuilder();
        builder.AppendLine($"Chiffre d’affaires encaissé {year}");
        if (!string.IsNullOrWhiteSpace(header.BusinessName)) builder.AppendLine(header.BusinessName + (string.IsNullOrWhiteSpace(header.Siret) ? "" : " — SIRET " + header.Siret));
        builder.AppendLine();
        builder.AppendLine("Mois;Vente de marchandises;Prestations de services;Total");
        foreach (var month in summary.Months)
            builder.AppendLine($"{MonthNames[month.Month - 1]};{Money(month.Goods)};{Money(month.Services)};{Money(month.Total)}");
        builder.AppendLine($"Total {year};{Money(summary.Goods)};{Money(summary.Services)};{Money(summary.Total)}");
        if (summary.CancelledCount > 0) builder.AppendLine($"{summary.CancelledCount} recette(s) annulée(s) exclue(s) des totaux.");
        return builder.ToString();
    }

    public static byte[] ToPdf(RevenueYear year, RevenueHeader header)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(header);
        var summary = RevenueTotals.Compute(year.Lines);
        var subtitle = new List<string>();
        var identity = string.Join(" — ", new[] { header.BusinessName, header.OwnerName, string.IsNullOrWhiteSpace(header.Siret) ? "" : "SIRET " + header.Siret }.Where(v => !string.IsNullOrWhiteSpace(v)));
        subtitle.Add(identity.Length > 0 ? identity : "Atelier non renseigné (Paramètres > Atelier)");
        if (header.ContactLine() is { } contact) subtitle.Add(contact);
        subtitle.Add($"Édité le {DateTime.Today:dd/MM/yyyy}. Les recettes annulées restent visibles, barrées, et sont exclues des totaux.");
        double[] x = [0, 52, 160, 285, 345, 410, 475, 540, 612, 690];
        int[] chars = [10, 23, 27, 12, 13, 13, 13, 14, 14, 17];
        var header0 = new PdfLine(["Date", "Client", "Libellé", "Nature", "Marchandises", "Prestations", "Montant", "Règlement", "Pièce", "Statut"], Bold: true);
        var rows = new List<PdfLine>();
        foreach (var line in year.Lines)
        {
            var e = line.Entry;
            rows.Add(new PdfLine([e.ReceivedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), e.ClientName ?? "", e.Label, NatureName(e.Nature), Money(e.GoodsAmount),
                Money(e.ServicesAmount), Money(e.Amount), e.PaymentMethod, e.InvoiceNumber, line.IsCancelled ? "Annulée : " + line.Cancellation!.Reason : "Valide"], Struck: line.IsCancelled));
        }
        rows.Add(new PdfLine([]));
        rows.Add(new PdfLine(["", "TOTAUX PAR MOIS", "", "", "Marchandises", "Prestations", "Total"], Bold: true));
        foreach (var month in summary.Months)
            rows.Add(new PdfLine(["", MonthNames[month.Month - 1], "", "", Money(month.Goods), Money(month.Services), Money(month.Total)]));
        rows.Add(new PdfLine(["", $"TOTAL {year.Year}", "", "", Money(summary.Goods), Money(summary.Services), Money(summary.Total)], Bold: true));
        return SimplePdfWriter.Write($"Livre des recettes {year.Year}", subtitle, header0, rows, x, chars);
    }

    private static string Number(decimal amount) => amount.ToString("0.00", French);

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = value.Replace("\r", " ").Replace("\n", " ");
        if ("=+-@\t".Contains(clean[0])) clean = "'" + clean;
        return clean.Contains(';') || clean.Contains('"') ? "\"" + clean.Replace("\"", "\"\"") + "\"" : clean;
    }
}

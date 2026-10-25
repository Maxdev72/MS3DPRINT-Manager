namespace MS3DPRINT.Manager.Core.Revenues;

public enum RevenueNature { Goods, Services, Mixed }

/// <summary>One collection recorded in the revenue book. <see cref="ReceivedOn"/> is the date the money was received, not the invoice date.</summary>
public sealed record RevenueEntry(Guid Id, DateOnly ReceivedOn, string Label, decimal Amount, RevenueNature Nature, decimal GoodsAmount, decimal ServicesAmount,
    string PaymentMethod, Guid? ClientId, string? ClientName, string? ProjectRef, string InvoiceNumber, string? InvoicePdfPath, DateTimeOffset RecordedAt);

public sealed record RevenueCancellation(Guid Id, Guid EntryId, string Reason, DateTimeOffset CancelledAt);

/// <summary>A traceable correction: the line keeps its identity, <see cref="Entry"/> holds the corrected values and the reason explains why.</summary>
public sealed record RevenueCorrection(Guid Id, Guid EntryId, string Reason, DateTimeOffset CorrectedAt, RevenueEntry Entry);

/// <param name="Entry">The values in force: the last correction when there is one.</param>
/// <param name="Original">The values first recorded; null when the line was never corrected.</param>
public sealed record RevenueLine(RevenueEntry Entry, RevenueCancellation? Cancellation, IReadOnlyList<RevenueCorrection>? Corrections = null, RevenueEntry? Original = null)
{
    public bool IsCancelled => Cancellation is not null;
    public bool IsCorrected => Corrections is { Count: > 0 };
    public RevenueEntry FirstRecorded => Original ?? Entry;
}

public static class RevenueHistory
{
    /// <summary>One readable sentence per correction: when, why and which fields changed.</summary>
    public static IReadOnlyList<string> Describe(RevenueLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var result = new List<string>();
        var previous = line.FirstRecorded;
        foreach (var correction in line.Corrections ?? [])
        {
            var changes = new List<string>();
            void Diff(string label, string? before, string? after) { if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal)) changes.Add($"{label} : {(string.IsNullOrEmpty(before) ? "—" : before)} → {(string.IsNullOrEmpty(after) ? "—" : after)}"); }
            var n = correction.Entry;
            Diff("date d’encaissement", previous.ReceivedOn.ToString("dd/MM/yyyy"), n.ReceivedOn.ToString("dd/MM/yyyy"));
            Diff("client", previous.ClientName, n.ClientName); Diff("libellé", previous.Label, n.Label);
            Diff("montant", RevenueExporter.Money(previous.Amount), RevenueExporter.Money(n.Amount));
            Diff("nature", RevenueExporter.NatureName(previous.Nature), RevenueExporter.NatureName(n.Nature));
            Diff("part marchandises", RevenueExporter.Money(previous.GoodsAmount), RevenueExporter.Money(n.GoodsAmount));
            Diff("règlement", previous.PaymentMethod, n.PaymentMethod); Diff("pièce", previous.InvoiceNumber, n.InvoiceNumber); Diff("projet", previous.ProjectRef, n.ProjectRef);
            Diff("justificatif", previous.InvoicePdfPath is null ? null : Path.GetFileName(previous.InvoicePdfPath), n.InvoicePdfPath is null ? null : Path.GetFileName(n.InvoicePdfPath));
            result.Add($"{correction.CorrectedAt:dd/MM/yyyy HH:mm} — {correction.Reason}" + (changes.Count > 0 ? " (" + string.Join(" ; ", changes) + ")" : ""));
            previous = n;
        }
        return result;
    }
}

public sealed record RevenueYear(int Year, IReadOnlyList<RevenueLine> Lines, IReadOnlyList<string> Errors);

public sealed record RevenueMonthTotal(int Month, decimal Goods, decimal Services)
{
    public decimal Total => Goods + Services;
}

public sealed record RevenueSummary(decimal Goods, decimal Services, IReadOnlyList<RevenueMonthTotal> Months, int CancelledCount)
{
    public decimal Total => Goods + Services;
}

public static class RevenueTotals
{
    public static RevenueSummary Compute(IEnumerable<RevenueLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var goods = new decimal[12];
        var services = new decimal[12];
        var cancelled = 0;
        foreach (var line in lines)
        {
            if (line.IsCancelled) { cancelled++; continue; }
            var month = line.Entry.ReceivedOn.Month - 1;
            goods[month] += line.Entry.GoodsAmount;
            services[month] += line.Entry.ServicesAmount;
        }
        var months = Enumerable.Range(0, 12).Select(i => new RevenueMonthTotal(i + 1, goods[i], services[i])).ToArray();
        return new(goods.Sum(), services.Sum(), months, cancelled);
    }
}

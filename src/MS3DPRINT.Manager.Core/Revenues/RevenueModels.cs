namespace MS3DPRINT.Manager.Core.Revenues;

public enum RevenueNature { Goods, Services, Mixed }

/// <summary>One collection recorded in the revenue book. <see cref="ReceivedOn"/> is the date the money was received, not the invoice date.</summary>
public sealed record RevenueEntry(Guid Id, DateOnly ReceivedOn, string Label, decimal Amount, RevenueNature Nature, decimal GoodsAmount, decimal ServicesAmount,
    string PaymentMethod, Guid? ClientId, string? ClientName, string? ProjectRef, string InvoiceNumber, string? InvoicePdfPath, DateTimeOffset RecordedAt);

public sealed record RevenueCancellation(Guid Id, Guid EntryId, string Reason, DateTimeOffset CancelledAt);

public sealed record RevenueLine(RevenueEntry Entry, RevenueCancellation? Cancellation)
{
    public bool IsCancelled => Cancellation is not null;
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

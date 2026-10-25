using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class RevenueTotalsTests
{
    private static RevenueLine Line(int month, decimal goods, decimal services, bool cancelled = false)
    {
        var entry = new RevenueEntry(Guid.NewGuid(), new DateOnly(2026, month, 5), "x", goods + services,
            goods > 0 && services > 0 ? RevenueNature.Mixed : goods > 0 ? RevenueNature.Goods : RevenueNature.Services,
            goods, services, "Virement", null, null, null, "F-" + month, null, DateTimeOffset.UtcNow);
        return new(entry, cancelled ? new RevenueCancellation(Guid.NewGuid(), entry.Id, "erreur", DateTimeOffset.UtcNow) : null);
    }

    [Fact]
    public void Totals_split_by_month_and_nature_and_ignore_cancelled_lines()
    {
        var summary = RevenueTotals.Compute([Line(1, 100m, 0), Line(1, 0, 50m), Line(3, 30m, 70m), Line(3, 999m, 0, cancelled: true)]);
        Assert.Equal(130m, summary.Goods);
        Assert.Equal(120m, summary.Services);
        Assert.Equal(250m, summary.Total);
        Assert.Equal(12, summary.Months.Count);
        Assert.Equal(150m, summary.Months[0].Total);
        Assert.Equal(0m, summary.Months[1].Total);
        Assert.Equal(100m, summary.Months[2].Total);
        Assert.Equal(1, summary.CancelledCount);
    }

    [Fact]
    public void Empty_year_gives_zero_totals()
    {
        var summary = RevenueTotals.Compute([]);
        Assert.Equal(0m, summary.Total);
        Assert.Equal(12, summary.Months.Count);
    }
}

using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class ThresholdsAndBalanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-thresholds-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static RevenueSummary Summary(decimal goods, decimal services)
    {
        var line = (RevenueNature nature, decimal g, decimal s) => new RevenueLine(new RevenueEntry(Guid.NewGuid(), new DateOnly(2026, 5, 5), "x", g + s, nature, g, s, "Virement", null, null, null, "F", null, DateTimeOffset.UtcNow), null);
        return RevenueTotals.Compute([line(RevenueNature.Mixed, goods, services)]);
    }

    [Fact]
    public void Only_filled_ceilings_are_watched_and_levels_follow_eighty_and_one_hundred_percent()
    {
        Assert.Empty(ActivityThresholdEvaluator.Evaluate(new ActivityThresholds(), Summary(1000, 1000)));
        var statuses = ActivityThresholdEvaluator.Evaluate(new ActivityThresholds(MicroTotalCeiling: 10_000, MicroServicesCeiling: 2_000, VatServicesThreshold: 1_500), Summary(3_000, 1_600));
        Assert.Equal(3, statuses.Count);
        Assert.Equal(ThresholdLevel.Ok, statuses[0].Level); Assert.Equal(46m, statuses[0].Percent); Assert.Equal(5_400m, statuses[0].Remaining);
        Assert.Equal(ThresholdLevel.Near, statuses[1].Level); Assert.Equal(80m, statuses[1].Percent);
        Assert.Equal(ThresholdLevel.Exceeded, statuses[2].Level); Assert.Equal(0m, statuses[2].Remaining);
    }

    [Fact]
    public void Cancelled_lines_do_not_count_toward_a_ceiling()
    {
        var entry = new RevenueEntry(Guid.NewGuid(), new DateOnly(2026, 5, 5), "x", 5000m, RevenueNature.Services, 0, 5000m, "Virement", null, null, null, "F", null, DateTimeOffset.UtcNow);
        var cancelled = new RevenueLine(entry, new RevenueCancellation(Guid.NewGuid(), entry.Id, "x", DateTimeOffset.UtcNow));
        var status = Assert.Single(ActivityThresholdEvaluator.Evaluate(new ActivityThresholds(MicroServicesCeiling: 1000), RevenueTotals.Compute([cancelled])));
        Assert.Equal(0m, status.Amount); Assert.Equal(ThresholdLevel.Ok, status.Level);
    }

    [Fact]
    public void Store_round_trips_refuses_non_positive_values_and_defaults_to_empty()
    {
        var store = new ActivityThresholdStore(_root);
        Assert.True(store.Load().IsEmpty);
        store.Save(new ActivityThresholds(MicroTotalCeiling: 90000.5m, VatServicesThreshold: 36000));
        var loaded = new ActivityThresholdStore(_root).Load();
        Assert.Equal(90000.5m, loaded.MicroTotalCeiling); Assert.Equal(36000m, loaded.VatServicesThreshold); Assert.Null(loaded.MicroServicesCeiling);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new ActivityThresholds(MicroTotalCeiling: 0)));
    }

    private static EstimateProfile Quote(string reference, decimal price, DateTimeOffset at) =>
        new(Guid.NewGuid(), "Devis", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, price, price), at, ProjectReference: reference);

    private static RevenueLine Collected(string? reference, decimal amount, bool cancelled = false)
    {
        var entry = new RevenueEntry(Guid.NewGuid(), new DateOnly(2026, 5, 5), "x", amount, RevenueNature.Services, 0, amount, "Virement", null, null, reference, "F", null, DateTimeOffset.UtcNow);
        return new(entry, cancelled ? new RevenueCancellation(Guid.NewGuid(), entry.Id, "x", DateTimeOffset.UtcNow) : null);
    }

    private static PurchaseLine Bought(string? reference, decimal amount, bool cancelled = false)
    {
        var entry = new PurchaseEntry(Guid.NewGuid(), new DateOnly(2026, 5, 5), "Bambu Lab", "Bobines", amount, "Carte", "A", null, reference, DateTimeOffset.UtcNow);
        return new(entry, cancelled ? new PurchaseCancellation(Guid.NewGuid(), entry.Id, "x", DateTimeOffset.UtcNow) : null);
    }

    [Fact]
    public void Project_balance_uses_the_latest_quote_collected_money_and_linked_purchases()
    {
        var now = DateTimeOffset.UtcNow;
        var balance = ProjectBalanceCalculator.Compute("ABC-2026-001",
            [Quote("ABC-2026-001", 100m, now.AddDays(-5)), Quote("ABC-2026-001", 150m, now), Quote("AUTRE-2026-001", 999m, now)],
            [Collected("abc-2026-001", 60m), Collected("ABC-2026-001", 40m, cancelled: true), Collected("AUTRE-2026-001", 500m), Collected(null, 70m)],
            [Bought("ABC-2026-001", 12.5m), Bought("ABC-2026-001", 99m, cancelled: true), Bought(null, 40m)]);
        Assert.Equal(150m, balance.Quoted); Assert.Equal(60m, balance.Collected); Assert.Equal(12.5m, balance.Purchases);
        Assert.Equal(90m, balance.RemainingToCollect); Assert.Equal(47.5m, balance.NetOfPurchases);
        Assert.Equal(1, balance.RevenueLines); Assert.Equal(1, balance.PurchaseLines); Assert.True(balance.HasData);
    }

    [Fact]
    public void Project_without_any_data_or_fully_collected_has_nothing_left_to_collect()
    {
        Assert.False(ProjectBalanceCalculator.Compute("ABC-2026-001", [], [Collected("X-1", 5m)], []).HasData);
        var paid = ProjectBalanceCalculator.Compute("ABC-2026-001", [Quote("ABC-2026-001", 80m, DateTimeOffset.UtcNow)], [Collected("ABC-2026-001", 80m)], []);
        Assert.Null(paid.RemainingToCollect);
    }
}

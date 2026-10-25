using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Projects;

/// <summary>What a project was quoted, what was really collected and what was bought for it.</summary>
public sealed record ProjectBalance(decimal? Quoted, decimal Collected, decimal Purchases, int RevenueLines, int PurchaseLines)
{
    public bool HasData => Quoted is not null || RevenueLines > 0 || PurchaseLines > 0;
    /// <summary>Quoted price not yet collected; null without a quote or when it is fully collected.</summary>
    public decimal? RemainingToCollect => Quoted is { } quoted && quoted > Collected ? quoted - Collected : null;
    /// <summary>Collected minus the purchases linked to the project (before social contributions and machine costs).</summary>
    public decimal NetOfPurchases => Collected - Purchases;
}

public static class ProjectBalanceCalculator
{
    /// <summary>The quote is the latest estimate saved for the project; cancelled revenues and purchases are ignored.</summary>
    public static ProjectBalance Compute(string projectReference, IEnumerable<EstimateProfile> estimates, IEnumerable<RevenueLine> revenues, IEnumerable<PurchaseLine> purchases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectReference);
        bool Same(string? value) => string.Equals(value?.Trim(), projectReference.Trim(), StringComparison.OrdinalIgnoreCase);
        var quote = estimates.Where(e => Same(e.ProjectReference)).OrderByDescending(e => e.CreatedAt).Select(e => (decimal?)e.Result.FinalPrice).FirstOrDefault();
        var collected = revenues.Where(l => !l.IsCancelled && Same(l.Entry.ProjectRef)).ToArray();
        var bought = purchases.Where(l => !l.IsCancelled && Same(l.Entry.ProjectRef)).ToArray();
        return new(quote, collected.Sum(l => l.Entry.Amount), bought.Sum(l => l.Entry.Amount), collected.Length, bought.Length);
    }
}

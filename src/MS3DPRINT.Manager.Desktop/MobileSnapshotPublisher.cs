using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Builds and publishes the read-only picture of the workshop that the phone consults. Names are shown as in the software.</summary>
public static class MobileSnapshotPublisher
{
    public static PcSnapshot Build(string workspaceRoot, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot);
        var year = now.Year;

        var clients = SafeList(() => new ClientCatalog(new ClientProfileStore(new WorkspaceMetadataPaths(root))).Load(root));
        var projects = SafeList(() => new WorkspaceProjectReader().Load(root));
        var estimates = SafeList(() => new EstimateStore(root).LoadReadable());

        var revenueJournal = new RevenueJournal(root);
        var purchaseJournal = new PurchaseJournal(root);
        var revenueLines = SafeList(() => revenueJournal.Years().SelectMany(y => revenueJournal.Load(y).Lines).ToArray());
        var purchaseLines = SafeList(() => purchaseJournal.Years().SelectMany(y => purchaseJournal.Load(y).Lines).ToArray());

        var collected = revenueLines.Where(l => !l.IsCancelled && !string.IsNullOrWhiteSpace(l.Entry.ProjectRef))
            .GroupBy(l => l.Entry.ProjectRef!.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(l => l.Entry.Amount), StringComparer.OrdinalIgnoreCase);
        var bought = purchaseLines.Where(l => !l.IsCancelled && !string.IsNullOrWhiteSpace(l.Entry.ProjectRef))
            .GroupBy(l => l.Entry.ProjectRef!.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(l => l.Entry.Amount), StringComparer.OrdinalIgnoreCase);
        var quotes = estimates.Where(e => !string.IsNullOrWhiteSpace(e.ProjectReference))
            .GroupBy(e => e.ProjectReference!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAt).First().Result.FinalPrice, StringComparer.OrdinalIgnoreCase);

        var snapshotProjects = projects.Select(p => new SnapshotProject(p.Reference, p.Title(), p.ClientTitle(), (p.Status ?? ProjectStatus.Quote).ToString(), p.Profile?.DueDate,
            p.Priority.ToString(), p.IsArchived, quotes.TryGetValue(p.Reference, out var q) ? q : null, collected.GetValueOrDefault(p.Reference), bought.GetValueOrDefault(p.Reference)))
            .OrderBy(p => p.Archived).ThenByDescending(p => p.Reference, StringComparer.OrdinalIgnoreCase).ToArray();

        var yearLines = SafeList(() => revenueJournal.Load(year).Lines);
        var summary = RevenueTotals.Compute(yearLines);
        var thresholds = SafeValue(() => new ActivityThresholdStore(root).Load(), new ActivityThresholds());
        var statuses = ActivityThresholdEvaluator.Evaluate(thresholds, summary)
            .Select(s => new SnapshotThreshold(s.Label, s.Amount, s.Limit, s.Level.ToString())).ToArray();
        var purchasesYear = PurchaseTotals.Compute(SafeList(() => purchaseJournal.Load(year).Lines));
        var pending = SafeValue(() => { var scan = MobileInbox.Scan(root); return scan.Items.Count + scan.Revenues.Count; }, 0);

        var suppliers = purchaseLines.Where(l => !l.IsCancelled).OrderByDescending(l => l.Entry.PurchasedOn).Select(l => l.Entry.Supplier.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Take(60).ToArray();
        var workshop = SafeValue(() => new RevenueHeaderStore(root).Load().BusinessName, "");

        return new PcSnapshot(now, string.IsNullOrWhiteSpace(workshop) ? "Mon atelier" : workshop.Trim(),
            clients.Select(c => new SnapshotClient(c.Title(), string.IsNullOrWhiteSpace(c.ClientCode) ? null : c.ClientCode)).ToArray(), snapshotProjects,
            new SnapshotActivity(year, summary.Total, summary.Goods, summary.Services, summary.Months.Select(m => m.Total).ToArray(), purchasesYear.Total, statuses, pending), suppliers);
    }

    /// <summary>Writes the picture into the exchange folder of the workspace.</summary>
    public static void Publish(string workspaceRoot) => PcSnapshotFile.Write(workspaceRoot, Build(workspaceRoot, DateTimeOffset.Now));

    // A damaged file must never stop the publication: the phone then simply sees fewer details.
    private static IReadOnlyList<T> SafeList<T>(Func<IReadOnlyList<T>> read)
    {
        try { return read(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { return []; }
    }

    private static T SafeValue<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { return fallback; }
    }
}

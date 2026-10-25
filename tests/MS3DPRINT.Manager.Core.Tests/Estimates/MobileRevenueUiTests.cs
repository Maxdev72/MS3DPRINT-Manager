using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private MobileRevenue DropMobileRevenue(Guid id, decimal amount, RevenueNature nature = RevenueNature.Services, decimal goods = 0m)
    {
        var revenue = new MobileRevenue(id, DateOnly.FromDateTime(DateTime.Today), "Impression de supports", amount, nature, goods, "Virement", "Atelier Martin", "F-12", null, null, DateTimeOffset.UtcNow);
        var folder = MobileInbox.FolderFor(_root); Directory.CreateDirectory(folder);
        var name = MobileRevenueExchange.BaseName(revenue);
        File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), [9, 9, 9]);
        revenue = revenue with { PhotoFileName = name + ".jpg" };
        File.WriteAllBytes(Path.Combine(folder, name + ".json"), MobileRevenueExchange.Serialize(revenue));
        return revenue;
    }

    [Fact]
    public Task Collections_noted_on_the_phone_are_imported_into_the_revenue_book_after_confirmation() => Run(async () =>
    {
        var first = DropMobileRevenue(Guid.NewGuid(), 120m);
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            Invoke(owner, "ImportMobileRevenues_Click", null, new RoutedEventArgs());
            await WaitForDialog<MobileRevenueInboxWindow>(owner);
            Assert.Contains(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Impression de supports") == true);
            DialogAction(owner, "Importer la sélection");
            var journal = new RevenueJournal(_root);
            for (var i = 0; i < 800 && journal.Years().Count == 0; i++) await Task.Delay(10);
            var entry = Assert.Single(journal.Load(DateTime.Today.Year).Lines).Entry;
            Assert.Equal(first.Id, entry.Id); Assert.Equal(120m, entry.Amount); Assert.Equal("F-12", entry.InvoiceNumber); Assert.Equal("Atelier Martin", entry.ClientName);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, entry.InvoicePdfPath));
            for (var i = 0; i < 800 && MobileInbox.Scan(_root).Revenues.Count > 0; i++) await Task.Delay(10);
            Assert.Empty(MobileInbox.Scan(_root).Revenues);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task The_pc_publishes_projects_clients_activity_and_pending_counts_for_the_phone() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Support caméra");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
        new EstimateStore(_root).Create(new EstimateProfile(Guid.NewGuid(), "Devis", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, 150m, 150m), DateTimeOffset.UtcNow, ProjectReference: project.Reference));
        new RevenueJournal(_root).Append(UiRevenue("F-1", 60m) with { ProjectRef = project.Reference });
        new PurchaseJournal(_root).Append(UiPurchase("A-1", 12.5m) with { ProjectRef = project.Reference, Supplier = "Bambu Lab" });
        new ActivityThresholdStore(_root).Save(new ActivityThresholds(MicroTotalCeiling: 100m));
        DropMobilePurchase(Guid.NewGuid(), "Leroy Merlin", 10m);
        await Task.CompletedTask;
        var snapshot = MobileSnapshotPublisher.Build(_root, DateTimeOffset.Now);
        var p = Assert.Single(snapshot.Projects);
        Assert.Equal(project.Reference, p.Reference); Assert.Equal("Support Caméra", p.Title, ignoreCase: true); Assert.Equal(150m, p.Quoted); Assert.Equal(60m, p.Collected); Assert.Equal(12.5m, p.Purchases);
        Assert.Contains(snapshot.Clients, c => c.Name.Contains("Horizon", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(60m, snapshot.Activity.Total); Assert.Equal(12, snapshot.Activity.Months.Count); Assert.Equal(12.5m, snapshot.Activity.PurchasesTotal);
        var threshold = Assert.Single(snapshot.Activity.Thresholds); Assert.Equal("Ok", threshold.Level);
        Assert.Equal(1, snapshot.Activity.PendingFromPhone); Assert.Contains("Bambu Lab", snapshot.Suppliers);
        MobileSnapshotPublisher.Publish(_root);
        Assert.NotNull(PcSnapshotFile.Read(_root));
    });
}

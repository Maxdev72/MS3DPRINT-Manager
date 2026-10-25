using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private MobilePurchase DropMobilePurchase(Guid id, string supplier, decimal amount)
    {
        var purchase = new MobilePurchase(id, DateOnly.FromDateTime(DateTime.Today), supplier, "Visserie", amount, "Carte bancaire", null, null, null, DateTimeOffset.UtcNow);
        var folder = MobileInbox.FolderFor(_root); Directory.CreateDirectory(folder);
        var name = MobileExchange.BaseName(purchase);
        File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), [1, 2, 3, 4]);
        purchase = purchase with { PhotoFileName = name + ".jpg" };
        File.WriteAllBytes(Path.Combine(folder, name + ".json"), MobileExchange.Serialize(purchase));
        return purchase;
    }

    [Fact]
    public Task Purchases_noted_on_the_phone_are_imported_after_confirmation_with_their_photo_and_filed_away() => Run(async () =>
    {
        var first = DropMobilePurchase(Guid.NewGuid(), "Leroy Merlin", 18.9m);
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            Assert.Contains("1 saisie du téléphone à importer", Field<TextBlock>(owner, "_purchasePageSummary").Text!);
            Invoke(owner, "ImportMobilePurchases_Click", null, new RoutedEventArgs());
            await WaitForDialog<MobileInboxWindow>(owner);
            Assert.Contains(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Leroy Merlin") == true);
            DialogAction(owner, "Importer la sélection");
            var journal = new PurchaseJournal(_root);
            for (var i = 0; i < 800 && journal.Years().Count == 0; i++) await Task.Delay(10);
            var entry = Assert.Single(journal.Load(DateTime.Today.Year).Lines).Entry;
            Assert.Equal(first.Id, entry.Id); Assert.Equal("Leroy Merlin", entry.Supplier); Assert.Equal(18.9m, entry.Amount);
            Assert.StartsWith("Ticket du", entry.Reference);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, entry.DocumentPath));
            for (var i = 0; i < 800 && MobileInbox.Scan(_root).Items.Count > 0; i++) await Task.Delay(10);
            Assert.Empty(MobileInbox.Scan(_root).Items);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(MobileInbox.FolderFor(_root), MobileExchange.ProcessedFolderName)).Length);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task A_phone_purchase_already_in_the_register_is_filed_away_without_a_duplicate() => Run(async () =>
    {
        var purchase = DropMobilePurchase(Guid.NewGuid(), "Bambu Lab", 40m);
        new PurchaseJournal(_root).Append(MobileExchange.ToEntry(purchase, null));
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            Invoke(owner, "ImportMobilePurchases_Click", null, new RoutedEventArgs());
            await WaitForDialog<MobileInboxWindow>(owner);
            DialogAction(owner, "Importer la sélection");
            for (var i = 0; i < 800 && MobileInbox.Scan(_root).Items.Count > 0; i++) await Task.Delay(10);
            Assert.Empty(MobileInbox.Scan(_root).Items);
            Assert.Single(new PurchaseJournal(_root).Load(DateTime.Today.Year).Lines);
        }
        finally { owner.Close(); }
    });
}

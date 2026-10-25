using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static PurchaseEntry UiPurchase(string reference, decimal amount) =>
        new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), "Bambu Lab", "Bobines PLA", amount, "Carte bancaire", reference, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public Task Purchase_register_page_lists_purchases_and_cancellation_needs_a_reason_and_keeps_the_line() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            var panel = owner.FindControl<Border>("PurchasesWorkspacePanel")!;
            Assert.True(panel.IsVisible);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Aucun achat en") == true);
            var journal = new PurchaseJournal(_root);
            var kept = UiPurchase("A-100", 40m); var wrong = UiPurchase("A-101", 15m);
            journal.Append(kept); journal.Append(wrong);
            await (Task)Invoke(owner, "LoadPurchasesAsync")!;
            var list = owner.FindControl<StackPanel>("PurchaseList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A-100");
            Assert.Contains("2 achats", Field<TextBlock>(owner, "_purchasePageSummary").Text!);

            Invoke(owner, "CancelPurchase_Click", new Button { Tag = wrong }, new RoutedEventArgs());
            await WaitForDialog<PurchaseCancelWindow>(owner);
            DialogAction(owner, "Annuler l’achat");
            Dispatcher.UIThread.RunJobs();
            Assert.False(journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled);
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single().Text = "Doublon";
            DialogAction(owner, "Annuler l’achat");
            for (var i = 0; i < 300 && !journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled; i++) await Task.Delay(10);
            Assert.Equal("Doublon", journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).Cancellation!.Reason);
            for (var i = 0; i < 300 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("annulé : Doublon") == true); i++) await Task.Delay(10);
            var struck = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("annulé : Doublon") == true);
            Assert.Equal(TextDecorations.Strikethrough, struck.TextDecorations);
        }
        finally { owner.Close(); }
    });
}

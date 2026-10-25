using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static TextBox FieldByPlaceholder(MainWindow owner, string placeholder)
        => owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == placeholder);

    [Fact]
    public Task Correcting_a_revenue_needs_a_reason_keeps_the_history_and_marks_the_line() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            var journal = new RevenueJournal(_root);
            var entry = UiRevenue("F-300", 80m);
            journal.Append(entry);
            await (Task)Invoke(owner, "LoadRevenuesAsync")!;
            Invoke(owner, "CorrectRevenue_Click", new Button { Tag = entry }, new RoutedEventArgs());
            await WaitForDialog<RevenueEntryWindow>(owner);
            Assert.Equal("80,00", FieldByPlaceholder(owner, "Montant encaissé").Text);
            FieldByPlaceholder(owner, "Montant encaissé").Text = "95,50";
            DialogAction(owner, "Enregistrer la correction");
            Dispatcher.UIThread.RunJobs();
            Assert.False(journal.Load(DateTime.Today.Year).Lines.Single().IsCorrected);
            FieldByPlaceholder(owner, "Ex. Montant erroné, mauvaise date").Text = "Montant erroné";
            DialogAction(owner, "Enregistrer la correction");
            for (var i = 0; i < 800 && !journal.Load(DateTime.Today.Year).Lines.Single().IsCorrected; i++) await Task.Delay(10);
            var line = journal.Load(DateTime.Today.Year).Lines.Single();
            Assert.Equal(95.5m, line.Entry.Amount); Assert.Equal(80m, line.FirstRecorded.Amount); Assert.Equal("Montant erroné", line.Corrections![0].Reason);
            var list = owner.FindControl<StackPanel>("RevenueList")!;
            for (var i = 0; i < 800 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.EndsWith("(corrigée)") == true); i++) await Task.Delay(10);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.EndsWith("(corrigée)") == true);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Correcting_a_purchase_keeps_the_history_and_the_annual_summary_uses_the_corrected_amount() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            var journal = new PurchaseJournal(_root);
            var entry = UiPurchase("A-300", 40m);
            journal.Append(entry);
            await (Task)Invoke(owner, "LoadPurchasesAsync")!;
            Invoke(owner, "CorrectPurchase_Click", new Button { Tag = entry }, new RoutedEventArgs());
            await WaitForDialog<PurchaseEntryWindow>(owner);
            FieldByPlaceholder(owner, "Montant payé").Text = "42,10";
            FieldByPlaceholder(owner, "Ex. Montant erroné, mauvaise date").Text = "Facture reçue";
            DialogAction(owner, "Enregistrer la correction");
            for (var i = 0; i < 800 && !journal.Load(DateTime.Today.Year).Lines.Single().IsCorrected; i++) await Task.Delay(10);
            var line = journal.Load(DateTime.Today.Year).Lines.Single();
            Assert.Equal(42.1m, line.Entry.Amount); Assert.Equal(40m, line.FirstRecorded.Amount);
            Assert.Contains("42,10 €", PurchaseExporter.AnnualSummary(DateTime.Today.Year, PurchaseTotals.Compute(journal.Load(DateTime.Today.Year).Lines), new RevenueHeader()));
        }
        finally { owner.Close(); }
    });
}

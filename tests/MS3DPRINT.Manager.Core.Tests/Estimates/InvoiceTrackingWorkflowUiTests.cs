using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Invoice_tracking_records_partial_then_final_payments_and_keeps_the_filter_and_search() => Run(async () =>
    {
        const string number = "FACT202610-52";
        WriteWorkspaceInvoice("Martin", number, "40,00", "vente de bien et de la prestation de service");
        var journal = new RevenueJournal(_root); journal.Append(UiRevenue(number, 10m));
        var owner = await Open(); owner.Show();
        try
        {
            await OpenInvoiceTracking(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            DialogAction(owner, "Toutes"); IdentityControl<TextBox>(body, "Rechercher une facture").Text = number;
            Assert.Equal(RevenueExporter.Money(30m), TrackedAmount(body, number, "Reste à encaisser"));
            ClickTrackedPayment(body, number); await WaitForDialog<InvoicePaymentWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            body = owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body, "Montant encaissé").Text = "10"; MixedGoods(body).Text = "4";
            await Task.Delay(280);
            CaptureDraftUi(owner, "invoice-tracking-payment");
            DialogAction(owner, "Enregistrer le paiement");
            var tracking = await WaitForDialog<InvoiceTrackingWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Equal(InvoiceTrackingFilter.All, tracking.Filter); Assert.Equal(number, tracking.SearchText);
            body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal(RevenueExporter.Money(20m), TrackedAmount(body, number, "Reste à encaisser"));
            ClickTrackedPayment(body, number); await WaitForDialog<InvoicePaymentWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal("20,00", IdentityControl<TextBox>(body, "Montant encaissé").Text);
            MixedGoods(body).Text = "8"; DialogAction(owner, "Enregistrer le paiement");
            await WaitForDialog<InvoiceTrackingWindow>(owner); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal(RevenueExporter.Money(0m), TrackedAmount(body, number, "Reste à encaisser"));
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Soldée");
            Assert.DoesNotContain(body.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Enregistrer un paiement pour " + number);
            var lines = journal.Load(DateTime.Today.Year).Lines; Assert.Equal(3, lines.Count); Assert.Equal(40m, lines.Sum(l => l.Entry.Amount));
            Assert.Equal(12m, lines.Skip(1).Sum(l => l.Entry.GoodsAmount));
            DialogAction(owner, "Fermer");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Cancelling_a_payment_or_a_changed_balance_does_not_add_an_unconfirmed_collection(bool stale) => Run(async () =>
    {
        const string number = "FACT202610-51";
        WriteWorkspaceInvoice("Martin", number, "40,00", "prestation de service");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenInvoiceTracking(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            ClickTrackedPayment(body, number); await WaitForDialog<InvoicePaymentWindow>(owner);
            var journal = new RevenueJournal(_root);
            if (stale) journal.Append(UiRevenue(number, 10m));
            DialogAction(owner, stale ? "Enregistrer le paiement" : "Annuler");
            await WaitForDialog<InvoiceTrackingWindow>(owner); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal(RevenueExporter.Money(stale ? 30m : 40m), TrackedAmount(body, number, "Reste à encaisser"));
            Assert.Equal(stale ? 1 : 0, journal.Years().SelectMany(year => journal.Load(year).Lines).Count());
            if (stale) Assert.Contains(owner.FindControl<StackPanel>("ToastHost")!.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("ont changé") == true);
            DialogAction(owner, "Fermer");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Bulk_import_remains_accessible_from_tracking_and_paid_invoices_are_still_consultable() => Run(async () =>
    {
        const string number = "FACT202610-51";
        WriteWorkspaceInvoice("Martin", number, "40,00", "prestation de service");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenInvoiceTracking(owner); Assert.Empty(new RevenueJournal(_root).Years());
            DialogAction(owner, "Importer plusieurs encaissements"); await WaitForDialog<RevenueBatchWindow>(owner);
            DialogAction(owner, "Tout cocher"); DialogAction(owner, "Enregistrer la sélection");
            await WaitForDialog<InvoiceTrackingWindow>(owner);
            DialogAction(owner, "Soldées"); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Single(TrackingRows(owner.FindControl<Border>("SidePanelBody")!));
            Assert.Single(new RevenueJournal(_root).Load(DateTime.Today.Year).Lines);
            DialogAction(owner, "Fermer");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    private static async Task OpenInvoiceTracking(MainWindow owner)
    {
        await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
        Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
        owner.GetVisualDescendants().OfType<Button>().Single(button => button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Factures"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForDialog<InvoiceTrackingWindow>(owner); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
    }
    private static void ClickTrackedPayment(Control body, string number) => body.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "Enregistrer un paiement pour " + number).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string? TrackedAmount(Control body, string number, string label) => TrackingRows(body)
        .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith(number) == true))
        .GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Children.OfType<TextBlock>().Any(text => text.Text == label))
        .Children.OfType<TextBlock>().Last().Text;
    private static void CloseInvoiceTestPanel(MainWindow owner)
    {
        if (typeof(MainWindow).GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner) is DialogWindow panel) panel.Close(false);
    }
}

using Avalonia;
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
    public Task Invoice_tracking_shows_unpaid_partial_and_paid_without_creating_any_collection() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "100,00", "prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-53", "30,00", "vente de bien");
        var journal = new RevenueJournal(_root); journal.Append(UiRevenue("FACT202610-52", 10m)); journal.Append(UiRevenue("FACT202610-53", 30m));
        var invoices = InvoiceReceivables.Build(InvoiceCatalog.Scan(_root).Candidates, journal.LoadInvoiceLines());
        var owner = await Open(); owner.Show();
        var dialog = new InvoiceTrackingWindow(invoices); var shown = dialog.ShowDialog<bool>(owner);
        var previousPreview = DesktopPreviewLauncher.OpenDocument; string? previewPath = null; DesktopPreviewLauncher.OpenDocument = path => previewPath = path;
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "À encaisser");
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Partiellement encaissée");
            Assert.Equal(2, TrackingRows(body).Length);
            DialogAction(owner, "Toutes"); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Equal(3, TrackingRows(body).Length);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Soldée");
            Assert.Equal(2, body.GetVisualDescendants().OfType<Button>().Count(b => AutomationProperties.GetName(b)?.StartsWith("Enregistrer un paiement pour ") == true));
            DialogAction(owner, "Soldées"); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var paid = Assert.Single(TrackingRows(body));
            Assert.Contains(paid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("FACT202610-53") == true);
            paid.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b)?.StartsWith("Voir la facture ") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(invoices.Single(i => i.State == InvoicePaymentState.Paid).Invoice.FilePath, previewPath);
            DialogAction(owner, "Toutes");
            foreach (var width in new[] { 960d, 1440d })
            { owner.Width = width; await Task.Delay(280); CaptureDraftUi(owner, "invoice-tracking-" + width); }
            Assert.Equal(2, journal.Load(2026).Lines.Count); Assert.Null(dialog.PaymentRequested);
        }
        finally { DesktopPreviewLauncher.OpenDocument = previousPreview; dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Invoice_payment_proposes_today_and_the_remaining_amount_then_records_only_the_confirmed_split() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var balance = Assert.Single(InvoiceReceivables.Build(InvoiceCatalog.Scan(_root).Candidates, [new(UiRevenue("FACT202610-52", 10m), null)]));
        var owner = await Open(); owner.Show();
        var dialog = new InvoicePaymentWindow(balance, new DateOnly(2026, 10, 26)); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); var body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal("26/10/2026", IdentityControl<TextBox>(body, "Date d’encaissement").Text);
            var amount = IdentityControl<TextBox>(body, "Montant encaissé"); Assert.Equal("30,00", amount.Text);
            amount.Text = "20"; MixedGoods(body).Text = "7,50"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(RevenueExporter.Money(12.5m), MixedServices(body).Text);
            DialogAction(owner, "Enregistrer le paiement"); Assert.True(await shown);
            var entry = Assert.IsType<RevenueEntry>(dialog.Result);
            Assert.Equal(20m, entry.Amount); Assert.Equal(7.5m, entry.GoodsAmount); Assert.Equal(12.5m, entry.ServicesAmount);
            Assert.Equal(new DateOnly(2026, 10, 26), entry.ReceivedOn); Assert.Equal("FACT202610-52", entry.InvoiceNumber);
            Assert.Equal(balance.Invoice.RelativePath, entry.InvoicePdfPath);
            Assert.Empty(new RevenueJournal(_root).Years());
        }
        finally { if (!shown.IsCompleted) dialog.Close(false); await shown; owner.Close(); }
    });

    [Theory]
    [InlineData("0")]
    [InlineData("31")]
    [InlineData("abc")]
    public Task Invoice_payment_refuses_invalid_or_excessive_amounts_without_closing(string value) => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "40,00", "prestation de service");
        var balance = Assert.Single(InvoiceReceivables.Build(InvoiceCatalog.Scan(_root).Candidates, [new(UiRevenue("FACT202610-51", 10m), null)]));
        var owner = await Open(); owner.Show(); var dialog = new InvoicePaymentWindow(balance, new DateOnly(2026, 10, 26)); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); var body = owner.FindControl<Border>("SidePanelBody")!;
            var amount = IdentityControl<TextBox>(body, "Montant encaissé"); amount.Text = value;
            DialogAction(owner, "Enregistrer le paiement"); Assert.False(shown.IsCompleted); Assert.Null(dialog.Result);
            Assert.Contains("form-invalid", amount.Classes);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Invoice_payment_preview_failure_is_visible_next_to_the_preview_button() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var balance = Assert.Single(InvoiceReceivables.Build(InvoiceCatalog.Scan(_root).Candidates, []));
        File.Delete(balance.Invoice.FilePath);
        var owner = await Open(); owner.Show(); var dialog = new InvoicePaymentWindow(balance, new DateOnly(2026, 10, 26)); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            await Task.Delay(280); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            body.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button)?.StartsWith("Voir la facture ") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var error = Assert.Single(body.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsVisible && text.Text?.Contains("introuvable") == true));
            var scroll = error.GetVisualAncestors().OfType<ScrollViewer>().First(); var top = error.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, 0, scroll.Bounds.Height); Assert.InRange(top + error.Bounds.Height, 1, scroll.Bounds.Height);
            Assert.Equal("40,00", IdentityControl<TextBox>(body, "Montant encaissé").Text); Assert.Null(dialog.Result);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });

    private static Border[] TrackingRows(Control body) => body.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("invoice-tracking-row")).ToArray();
}

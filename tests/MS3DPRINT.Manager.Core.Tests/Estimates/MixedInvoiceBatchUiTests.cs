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
    public Task Mixed_invoice_import_requires_a_split_then_records_goods_and_the_computed_services() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            Invoke(owner, "ImportInvoices_Click", null, new RoutedEventArgs());
            await WaitForDialog<RevenueBatchWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var boxes = body.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.All(boxes, box => { Assert.True(box.IsEnabled); Assert.NotEqual(true, box.IsChecked); });
            DialogAction(owner, "Tout cocher"); Assert.All(boxes, box => Assert.True(box.IsChecked));
            DialogAction(owner, "Enregistrer la sélection"); Dispatcher.UIThread.RunJobs();
            Assert.Empty(new RevenueJournal(_root).Years());
            var goods = MixedGoods(body); goods.Text = "24,99";
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Equal(RevenueExporter.Money(15.01m), MixedServices(body).Text);
            foreach (var width in new[] { 960d, 1440d })
            { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "mixed-invoice-split-" + width); }
            DialogAction(owner, "Enregistrer la sélection");
            var journal = new RevenueJournal(_root);
            for (var i = 0; i < 200 && journal.Load(2026).Lines.Count < 2; i++) await Task.Delay(10);
            var lines = journal.Load(2026).Lines; Assert.Equal(2, lines.Count);
            var mixed = lines.Single(l => l.Entry.InvoiceNumber == "FACT202610-52").Entry;
            Assert.Equal(RevenueNature.Mixed, mixed.Nature); Assert.Equal(40m, mixed.Amount);
            Assert.Equal(24.99m, mixed.GoodsAmount); Assert.Equal(15.01m, mixed.ServicesAmount);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, mixed.InvoicePdfPath));
            var totals = RevenueTotals.Compute(lines);
            Assert.Equal(24.99m, totals.Goods); Assert.Equal(100.32m, totals.Services);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("40")]
    [InlineData("41")]
    [InlineData("39,999")]
    [InlineData("0,004")]
    public Task Mixed_invoice_rejects_invalid_splits_and_preserves_the_form_for_correction(string value) => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var owner = await Open(); owner.Show();
        var dialog = new RevenueBatchWindow(InvoiceCatalog.Scan(_root).Candidates, 0, new DateOnly(2026, 10, 26));
        var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var check = Assert.Single(body.GetVisualDescendants().OfType<CheckBox>());
            Assert.True(check.IsEnabled); check.IsChecked = true;
            var goods = MixedGoods(body); goods.Text = value;
            DialogAction(owner, "Enregistrer la sélection"); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.False(shown.IsCompleted); Assert.Empty(dialog.Result); Assert.True(check.IsChecked);
            Assert.Contains("form-invalid", goods.Classes);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text?.Contains("part marchandises") == true);
            goods.Text = "20.50"; Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("form-invalid", goods.Classes);
            Assert.Equal(RevenueExporter.Money(19.5m), MixedServices(body).Text);
            DialogAction(owner, "Enregistrer la sélection"); Assert.True(await shown);
            var entry = Assert.Single(dialog.Result);
            Assert.Equal(20.5m, entry.GoodsAmount); Assert.Equal(19.5m, entry.ServicesAmount);
        }
        finally { if (!shown.IsCompleted) dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Invoice_nature_can_be_changed_and_hidden_mixed_values_do_not_affect_a_goods_invoice() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var owner = await Open(); owner.Show();
        var dialog = new RevenueBatchWindow(InvoiceCatalog.Scan(_root).Candidates, 0, new DateOnly(2026, 10, 26));
        var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var nature = body.GetVisualDescendants().OfType<ComboBox>().First();
            var goods = MixedGoods(body); goods.Text = "invalide";
            nature.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.False(goods.IsEffectivelyVisible);
            Assert.Single(body.GetVisualDescendants().OfType<CheckBox>()).IsChecked = true;
            DialogAction(owner, "Enregistrer la sélection"); Assert.True(await shown);
            var entry = Assert.Single(dialog.Result);
            Assert.Equal(RevenueNature.Goods, entry.Nature); Assert.Equal(40m, entry.GoodsAmount); Assert.Equal(0m, entry.ServicesAmount);
        }
        finally { if (!shown.IsCompleted) dialog.Close(false); await shown; owner.Close(); }
    });

    private static TextBox MixedGoods(Control body) => Assert.Single(body.GetVisualDescendants().OfType<TextBox>()
        .Where(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Part marchandises"));
    private static TextBlock MixedServices(Control body) => Assert.Single(body.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Part prestations calculée"));
}

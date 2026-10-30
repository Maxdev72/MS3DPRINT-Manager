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
    public Task Invoice_import_previews_every_pdf_and_preserves_the_form_in_a_narrow_panel() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        var invoices = InvoiceCatalog.Scan(_root).Candidates;
        var previous = DesktopPreviewLauncher.OpenDocument; var paths = new List<string>();
        DesktopPreviewLauncher.OpenDocument = path => paths.Add(path);
        var owner = await Open(); owner.Show();
        var dialog = new RevenueBatchWindow(invoices, 0, new DateOnly(2026, 10, 26));
        var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var previews = body.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b)?.StartsWith("Voir la facture ") == true).ToArray();
            Assert.Equal(2, previews.Length);
            var check = body.GetVisualDescendants().OfType<CheckBox>().First(); check.IsChecked = true;
            var date = body.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Date d’encaissement"); date.Text = "09/10/2026";
            var choices = body.GetVisualDescendants().OfType<ComboBox>().Take(2).ToArray();
            choices[0].SelectedIndex = 1; choices[1].SelectedItem = "Carte bancaire";
            foreach (var preview in previews)
            {
                Assert.True(preview.IsEffectivelyEnabled);
                preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(invoices.Select(i => i.FilePath), paths);
            Assert.True(check.IsChecked); Assert.Equal("09/10/2026", date.Text);
            Assert.Equal(1, choices[0].SelectedIndex); Assert.Equal("Carte bancaire", choices[1].SelectedItem);
            Assert.False(shown.IsCompleted); Assert.Empty(dialog.Result);
            Assert.Empty(new RevenueJournal(_root).Years());
            foreach (var width in new[] { 960d, 1440d })
            {
                owner.Width = width; await Task.Delay(280); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                foreach (var control in previews.Cast<Control>().Concat(body.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible)).Concat(choices))
                {
                    var left = control.TranslatePoint(default, body)!.Value.X;
                    Assert.InRange(left, 0, body.Bounds.Width);
                    Assert.InRange(left + control.Bounds.Width, 1, body.Bounds.Width);
                }
                var title = body.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("FACT202610-51") == true);
                Assert.True(title.Bounds.Width > 80);
                Assert.InRange(title.TranslatePoint(default, body)!.Value.X, 0, body.Bounds.Width);
                CaptureDraftUi(owner, "invoice-batch-preview-" + width);
            }
        }
        finally { DesktopPreviewLauncher.OpenDocument = previous; dialog.Close(false); await shown; owner.Close(); }
    });

    [Theory]
    [InlineData("missing")]
    [InlineData("error")]
    [InlineData("unavailable")]
    public Task Invoice_preview_errors_keep_the_import_open_and_do_not_record_anything(string failure) => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        var invoices = InvoiceCatalog.Scan(_root).Candidates;
        var previous = DesktopPreviewLauncher.OpenDocument; var calls = 0;
        DesktopPreviewLauncher.OpenDocument = failure == "unavailable" ? null : _ => { calls++; throw new IOException("PDF verrouillé"); };
        if (failure == "missing") File.Delete(invoices[0].FilePath);
        var owner = await Open(); owner.Show();
        var dialog = new RevenueBatchWindow(invoices, 0, new DateOnly(2026, 10, 26)); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var button = Assert.Single(body.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b)?.StartsWith("Voir la facture ") == true));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible &&
                (text.Text?.Contains("introuvable") == true || text.Text?.Contains("Impossible d’afficher") == true));
            Assert.Equal(failure == "error" ? 1 : 0, calls);
            Assert.False(shown.IsCompleted); Assert.Empty(dialog.Result); Assert.Empty(new RevenueJournal(_root).Years());
        }
        finally { DesktopPreviewLauncher.OpenDocument = previous; dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Invoice_preview_error_is_visible_next_to_the_invoice_in_a_long_batch() => Run(async () =>
    {
        for (var i = 0; i < 6; i++) WriteWorkspaceInvoice("Martin", "FACT202610-" + (51 + i), "85,31", "prestation de service");
        var invoices = InvoiceCatalog.Scan(_root).Candidates; File.Delete(invoices[0].FilePath);
        var owner = await Open(); owner.Show();
        var dialog = new RevenueBatchWindow(invoices, 0, new DateOnly(2026, 10, 26)); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            await Task.Delay(280); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            body.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b)?.StartsWith("Voir la facture ") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var error = Assert.Single(body.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Text?.Contains("introuvable") == true));
            var scroll = error.GetVisualAncestors().OfType<ScrollViewer>().First();
            var top = error.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, 0, scroll.Bounds.Height);
            Assert.InRange(top + error.Bounds.Height, 1, scroll.Bounds.Height);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });
}

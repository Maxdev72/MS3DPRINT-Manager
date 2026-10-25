using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static RevenueEntry UiRevenue(string invoice, decimal amount) =>
        new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), "Impression de supports", amount, RevenueNature.Services, 0, amount, "Virement", null, "Atelier Test", null, invoice, null, DateTimeOffset.UtcNow);

    [Fact]
    public Task Revenue_book_page_lists_entries_and_cancellation_needs_a_reason_and_keeps_the_line() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            var panel = owner.FindControl<Border>("RevenuesWorkspacePanel")!;
            Assert.True(panel.IsVisible);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Aucune recette en") == true);

            var journal = new RevenueJournal(_root);
            var kept = UiRevenue("F-100", 80m); var wrong = UiRevenue("F-101", 20m);
            journal.Append(kept); journal.Append(wrong);
            await (Task)Invoke(owner, "LoadRevenuesAsync")!;
            var list = owner.FindControl<StackPanel>("RevenueList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "F-100");
            Assert.Contains("2 recettes", Field<TextBlock>(owner, "_revenuePageSummary").Text!);

            var cancel = new Button { Tag = wrong };
            Invoke(owner, "CancelRevenue_Click", cancel, new RoutedEventArgs());
            await WaitForDialog<RevenueCancelWindow>(owner);
            DialogAction(owner, "Annuler la recette");
            Dispatcher.UIThread.RunJobs();
            Assert.False(journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled);
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single().Text = "Doublon";
            DialogAction(owner, "Annuler la recette");
            for (var i = 0; i < 200 && !journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled; i++) await Task.Delay(10);

            var toasts = string.Join(" | ", owner.FindControl<StackPanel>("ToastHost")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            var line = journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id);
            Assert.True(line.IsCancelled, toasts);
            Assert.True(line.IsCancelled); Assert.Equal("Doublon", line.Cancellation!.Reason);
            for (var i = 0; i < 200 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("annulée : Doublon") == true); i++) await Task.Delay(10);
            var struck = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("annulée : Doublon") == true);
            Assert.Equal(TextDecorations.Strikethrough, struck.TextDecorations);
            Assert.Contains("1 recette ", Field<TextBlock>(owner, "_revenuePageSummary").Text!);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Dropped_invoice_pdf_prefills_the_entry_and_the_saved_line_keeps_a_copy_of_the_pdf() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var pdf = Path.Combine(Path.GetTempPath(), "Facture-FACT202610-77-" + Guid.NewGuid().ToString("N") + ".pdf");
            var rows = new List<PdfLine>
            {
                new(["", "", "", "", "", "", "", "Atelier Martin"]),
                new(["Facture FACT202610-77"]), new(["émise le 08/10/2026"]), new(["Total TTC 85,31"]), new(["Cette facture concerne de la prestation de service."]),
            };
            File.WriteAllBytes(pdf, SimplePdfWriter.Write("MS3DPRINT", [], new PdfLine(["x"]), rows,
                [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]));
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            _ = (Task)Invoke(owner, "OpenRevenueEntryAsync", pdf, null)!;
            var dialog = await WaitForDialog<RevenueEntryWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            for (var i = 0; i < 100 && !body.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text == "FACT202610-77"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Contains(body.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "FACT202610-77");
            Assert.Contains(body.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "85,31");
            DialogAction(owner, "Enregistrer");
            var journal = new RevenueJournal(_root);
            for (var i = 0; i < 300 && journal.Years().Count == 0; i++) await Task.Delay(10);
            var entry = Assert.Single(journal.Load(DateTime.Today.Year).Lines).Entry;
            Assert.Equal("FACT202610-77", entry.InvoiceNumber); Assert.Equal(85.31m, entry.Amount); Assert.Equal("Atelier Martin", entry.ClientName);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, entry.InvoicePdfPath));
            Assert.True(File.Exists(pdf));
            File.Delete(pdf);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Project_suggestion_prefills_client_reference_label_and_quoted_price_without_saving() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            _ = (Task)Invoke(owner, "OpenRevenueEntryAsync", null, new RevenueSuggestion("Atelier Martin", null, "2026-0042", "Support caméra", 123m))!;
            await WaitForDialog<RevenueEntryWindow>(owner);
            var texts = owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToArray();
            Assert.Contains("2026-0042", texts); Assert.Contains("Support caméra", texts); Assert.Contains("123,00", texts);
            DialogAction(owner, "Annuler");
            await Task.Delay(30);
            Assert.Empty(new RevenueJournal(_root).Years());
        }
        finally { owner.Close(); }
    });
}

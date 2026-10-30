using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Purchase_receipts_follow_the_supplier_on_creation_and_correction(bool correction) => Run(async () =>
    {
        var source = Path.Combine(_root, "facture-test.pdf"); File.WriteAllText(source, "Justificatif test");
        var journal = new PurchaseJournal(_root);
        var legacy = RevenueInvoiceArchive.Store(_root, source, null, DateTime.Today.Year, "purchases");
        var original = UiPurchase("DOC-001", 20m) with { Supplier = "Ancien fournisseur", DocumentPath = legacy };
        if (correction) journal.Append(original);
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            if (correction) Invoke(owner, "CorrectPurchase_Click", new Button { Tag = original }, new RoutedEventArgs());
            else Invoke(owner, "CreatePurchase_Click", null, new RoutedEventArgs());
            var dialog = await WaitForDialog<PurchaseEntryWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<ComboBox>(body, "Fournisseur").Text = "Nouveau fournisseur";
            if (correction) IdentityControl<TextBox>(body, "Motif de la correction").Text = "Correction du fournisseur";
            else
            {
                IdentityControl<TextBox>(body, "Libellé").Text = "Bobines";
                IdentityControl<TextBox>(body, "Montant payé").Text = "20";
                IdentityControl<TextBox>(body, "Pièce justificative").Text = "DOC-001";
                typeof(PurchaseEntryWindow).GetProperty(nameof(PurchaseEntryWindow.SourceDocumentPath))!.SetValue(dialog, source);
            }
            DialogAction(owner, correction ? "Enregistrer la correction" : "Enregistrer");
            for (var attempt = 0; attempt < 200 && !journal.Load(DateTime.Today.Year).Lines.Any(l => l.Entry.Supplier == "Nouveau fournisseur"); attempt++)
                await Task.Delay(10);
            var line = Assert.Single(journal.Load(DateTime.Today.Year).Lines);
            Assert.Equal("Nouveau fournisseur", line.Entry.Supplier);
            Assert.StartsWith(Path.Combine("06_FOURNISSEURS", "NOUVEAU_FOURNISSEUR", "04_FACTURES"), line.Entry.DocumentPath);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, line.Entry.DocumentPath));
            Assert.True(File.Exists(source));
            if (correction)
            {
                Assert.Single(line.Corrections!);
                Assert.Equal(legacy, line.FirstRecorded.DocumentPath);
                Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, legacy));
            }
        }
        finally { owner.Close(); }
    });

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
            for (var i = 0; i < 800 && !journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled; i++) await Task.Delay(10);
            Assert.Equal("Doublon", journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).Cancellation!.Reason);
            for (var i = 0; i < 800 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("annulé : Doublon") == true); i++) await Task.Delay(10);
            var struck = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("annulé : Doublon") == true);
            Assert.Equal(TextDecorations.Strikethrough, struck.TextDecorations);
        }
        finally { owner.Close(); }
    });
}

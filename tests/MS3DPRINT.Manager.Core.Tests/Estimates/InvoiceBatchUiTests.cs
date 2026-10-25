using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private void WriteWorkspaceInvoice(string client, string number, string total, string nature)
    {
        var path = Path.Combine(_root, "CLIENTS", client, "Documents", $"Facture-{number}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rows = new List<PdfLine>
        {
            new(["", "", "", "", "", "", "", "Atelier Martin"]),
            new([$"Facture {number}"]), new(["émise le 08/10/2026"]), new([$"Total TTC {total}"]), new([$"Cette facture concerne de la {nature.Split(" et ")[0]}"]), new([nature.Contains(" et ") ? "et " + nature.Split(" et ")[1] + "." : ""]),
        };
        File.WriteAllBytes(path, SimplePdfWriter.Write("MS3DPRINT", [], new PdfLine(["x"]), rows,
            [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]));
    }

    [Fact]
    public Task Invoices_without_a_revenue_are_listed_unchecked_and_only_ticked_ones_are_recorded_with_their_pdf() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "vente de bien et de la prestation de service");
        WriteWorkspaceInvoice("Dupont", "FACT202610-53", "12,00", "vente de bien");
        var journal = new RevenueJournal(_root);
        journal.Append(UiRevenue("FACT202610-53", 12m));
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            Invoke(owner, "ImportInvoices_Click", null, new RoutedEventArgs());
            var dialog = await WaitForDialog<RevenueBatchWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var texts = body.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.Contains(texts, t => t.StartsWith("FACT202610-51"));
            Assert.Contains(texts, t => t.StartsWith("FACT202610-52") && t.Contains("Facture mixte"));
            Assert.DoesNotContain(texts, t => t.StartsWith("FACT202610-53"));
            var boxes = body.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(2, boxes.Length); Assert.All(boxes, b => Assert.NotEqual(true, b.IsChecked));
            Assert.Single(boxes, b => b.IsEnabled);
            DialogAction(owner, "Enregistrer la sélection");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(journal.Load(2026).Lines);
            boxes.Single(b => b.IsEnabled).IsChecked = true;
            DialogAction(owner, "Enregistrer la sélection");
            for (var i = 0; i < 800 && journal.Load(2026).Lines.Count < 2; i++) await Task.Delay(10);
            var added = journal.Load(2026).Lines.Select(l => l.Entry).Single(x => x.InvoiceNumber == "FACT202610-51");
            Assert.Equal(85.31m, added.Amount); Assert.Equal(new DateOnly(2026, 10, 8), added.ReceivedOn);
            Assert.Equal(RevenueNature.Services, added.Nature); Assert.Equal("Atelier Martin", added.ClientName);
            Assert.Equal(Path.Combine("CLIENTS", "Martin", "Documents", "Facture-FACT202610-51.pdf"), added.InvoicePdfPath);
            Assert.NotNull(RevenueInvoiceArchive.Resolve(_root, added.InvoicePdfPath));
        }
        finally { owner.Close(); }
    });
}

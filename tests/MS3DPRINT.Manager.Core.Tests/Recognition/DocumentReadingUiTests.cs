using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Recognition;

/// <summary>The entry windows read a picture dropped, pasted or chosen, fill the fields and say what to check.</summary>
[Collection("DocumentReader statics")]
public sealed class DocumentReadingUiTests : IDisposable
{
    private readonly IOcrEngine? _ocr = DocumentReader.Ocr;
    private readonly AnalysisContext _context = DocumentReadingUi.Context;
    private readonly string _file = Path.Combine(Path.GetTempPath(), "ms3d-ui-" + Guid.NewGuid().ToString("N") + ".png");

    public void Dispose() { DocumentReader.Ocr = _ocr; DocumentReadingUi.Context = _context; if (File.Exists(_file)) File.Delete(_file); }

    private sealed class FakeOcr(string text) : IOcrEngine
    {
        public Task<IReadOnlyList<OcrText>> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OcrText>>([new OcrText(text, "")]);
    }

    private static async Task Run(Func<Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () => { await test(); return true; }, CancellationToken.None);
    }

    private static TextBox Box(Window window, string placeholder) => window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == placeholder);

    [Fact]
    public Task A_receipt_picture_fills_the_purchase_form_and_is_attached() => Run(async () =>
    {
        File.WriteAllBytes(_file, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
        DocumentReader.Ocr = new FakeOcr("LEROY MERLIN LE MANS\nTicket n° 004512\n24/10/2026 14:32\nEQUERRE ACIER  3,00\nTOTAL TTC  14,40\nCB SANS CONTACT 14,40");
        DocumentReadingUi.Context = new AnalysisContext { OwnBusinessName = "MS3DPRINT", Today = new DateOnly(2026, 10, 26) };
        var window = new PurchaseEntryWindow(["Leroy Merlin"], new DateOnly(2026, 10, 26));
        window.Show();
        try
        {
            await window.LoadDocumentAsync(_file);
            Assert.Equal("14,40", Box(window, "Montant payé").Text);
            Assert.Equal("24/10/2026", Box(window, "jj/mm/aaaa").Text);
            Assert.Equal("004512", Box(window, "Numéro de facture ou de commande").Text);
            Assert.Equal(_file, window.SourceDocumentPath);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Document lu") == true);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task A_supplier_invoice_opened_from_the_revenue_form_warns_it_is_a_purchase() => Run(async () =>
    {
        File.WriteAllBytes(_file, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
        DocumentReader.Ocr = new FakeOcr("POLYMAKER SAS\nSIRET 552 081 317 00011\nFacture n° FA-2026-004521\nDate : 17/10/2026\nFacturé à\nMS3DPRINT\nTotal HT 65,70\nTVA 20 % 13,14\nTotal TTC 78,84");
        DocumentReadingUi.Context = new AnalysisContext { OwnBusinessName = "MS3DPRINT", Today = new DateOnly(2026, 10, 26) };
        var window = new RevenueEntryWindow([], new DateOnly(2026, 10, 26));
        window.Show();
        try
        {
            await window.LoadInvoiceAsync(_file);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("page Achats") == true);
        }
        finally { window.Close(); }
    });
}

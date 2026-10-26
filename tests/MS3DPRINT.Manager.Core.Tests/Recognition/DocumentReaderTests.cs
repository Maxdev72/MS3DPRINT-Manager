using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Recognition;

[Collection("DocumentReader statics")]
public sealed class DocumentReaderTests : IDisposable
{
    private static readonly AnalysisContext Me = new() { OwnBusinessName = "MS3DPRINT", Today = new DateOnly(2026, 10, 26) };
    private readonly IOcrEngine? _ocr = DocumentReader.Ocr;
    private readonly IPdfRasterizer? _rasterizer = DocumentReader.Rasterizer;

    public void Dispose() { DocumentReader.Ocr = _ocr; DocumentReader.Rasterizer = _rasterizer; }

    private sealed class FakeOcr(params string[] variants) : IOcrEngine
    {
        public int Calls;
        public Task<IReadOnlyList<OcrText>> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult<IReadOnlyList<OcrText>>(variants.Select((t, i) => new OcrText(t, "essai " + (i + 1))).ToList()); }
    }

    private sealed class FakeRasterizer(int pages) : IPdfRasterizer
    {
        public Task<IReadOnlyList<byte[]>> RenderPagesAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<byte[]>>(Enumerable.Range(0, Math.Min(pages, maxPages)).Select(_ => new byte[] { 1, 2, 3 }).ToList());
    }

    private static byte[] Pdf(params string[] rows)
        => SimplePdfWriter.Write("MS3DPRINT", [], new PdfLine(["x"]), rows.Select(r => new PdfLine([r])).ToList(), [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]);

    [Fact]
    public async Task A_pdf_with_text_is_read_without_any_ocr()
    {
        DocumentReader.Ocr = null;
        var reading = await DocumentReader.ReadAsync(Pdf("Boulangerie Dupain", "Ticket 55", "12/10/2026", "TOTAL 8,40"), "a.pdf", Me);
        Assert.Equal(8.40m, reading.TotalTtc!.Value);
        Assert.Equal("PDF", reading.Source);
    }

    [Fact]
    public async Task The_best_of_several_ocr_variants_wins()
    {
        var bad = "Bncuiag\nxx\n12 y";
        var good = "Leroy Merlin\nTicket n° 99887\n12/10/2026\nTOTAL TTC 18,90\nCB 18,90";
        DocumentReader.Ocr = new FakeOcr(bad, good);
        var reading = await DocumentReader.ReadAsync([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], "screen.png", Me);
        Assert.Equal(18.90m, reading.TotalTtc!.Value);
        Assert.Equal("OCR (essai 2)", reading.Source);
    }

    [Fact]
    public async Task A_scanned_pdf_is_rendered_and_read_by_ocr()
    {
        DocumentReader.Ocr = new FakeOcr("Castorama\nTicket 12\n11/10/2026\nTOTAL 42,00");
        DocumentReader.Rasterizer = new FakeRasterizer(1);
        var reading = await DocumentReader.ReadAsync(Pdf("."), "scan.pdf", Me);
        Assert.Equal(42.00m, reading.TotalTtc!.Value);
        Assert.StartsWith("PDF numérisé", reading.Source);
    }

    [Fact]
    public async Task An_image_without_ocr_or_with_unreadable_text_explains_why()
    {
        DocumentReader.Ocr = null;
        Assert.Contains("pas disponible", (await DocumentReader.ReadAsync([1, 2, 3, 4, 5], "x.jpg", Me)).Warnings[0]);
        DocumentReader.Ocr = new FakeOcr("~ ."); DocumentReader.Rasterizer = null;
        Assert.Contains("Aucun texte", (await DocumentReader.ReadAsync([1, 2, 3, 4, 5], "x.jpg", Me)).Warnings[0]);
    }

    [Fact]
    public async Task Indy_layout_rules_win_for_the_users_own_invoices()
    {
        DocumentReader.Ocr = null;
        var pdf = Pdf("Facture FACT202610-33", "émise le 08/10/2026", "Total HT 66,17", "TVA non applicable, art. 293 B du CGI", "Total TTC 66,17", "vente de bien");
        var reading = await DocumentReader.ReadAsync(pdf, "f.pdf", Me);
        Assert.Equal("FACT202610-33", reading.Number!.Value);
        Assert.Equal(new DateOnly(2026, 10, 8), reading.Date!.Value);
        Assert.Equal(66.17m, reading.TotalTtc!.Value);
        Assert.Equal(DocumentDirection.Revenue, reading.Direction);
        Assert.Equal(RevenueNature.Goods, reading.Nature);
    }

    [Fact]
    public async Task Missing_files_do_not_throw()
    {
        var reading = await DocumentReader.ReadAsync(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid() + ".pdf"), Me);
        Assert.True(reading.IsEmpty);
        Assert.Single(reading.Warnings);
    }
}

[CollectionDefinition("DocumentReader statics", DisableParallelization = true)]
public sealed class DocumentReaderStaticsCollection;

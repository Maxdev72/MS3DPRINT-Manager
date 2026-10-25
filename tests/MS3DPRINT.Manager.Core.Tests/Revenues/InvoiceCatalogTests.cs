using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class InvoiceCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-invoices-" + Guid.NewGuid().ToString("N"));
    public InvoiceCatalogTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Invoice(string relative, string number, string total, string issued = "08/10/2026", string nature = "prestation de service")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rows = new List<PdfLine>
        {
            new(["", "", "", "", "", "", "", "Atelier Martin"]),
            new([$"Facture {number}"]), new([$"émise le {issued}"]), new([$"Total TTC {total}"]), new([$"Cette facture concerne de la {nature}."]),
        };
        File.WriteAllBytes(path, SimplePdfWriter.Write("MS3DPRINT", [], new PdfLine(["x"]), rows,
            [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]));
        return path;
    }

    private static RevenueLine Recorded(string invoice, bool cancelled = false)
    {
        var entry = new RevenueEntry(Guid.NewGuid(), new DateOnly(2026, 10, 9), "x", 10m, RevenueNature.Services, 0, 10m, "Virement", null, null, null, invoice, null, DateTimeOffset.UtcNow);
        return new(entry, cancelled ? new RevenueCancellation(Guid.NewGuid(), entry.Id, "x", DateTimeOffset.UtcNow) : null);
    }

    [Fact]
    public void Scan_finds_invoices_anywhere_reads_them_and_guesses_the_client_folder()
    {
        Invoice(Path.Combine("CLIENTS", "Martin", "Documents", "Facture-FACT202610-33.pdf"), "FACT202610-33", "85,31");
        Invoice(Path.Combine("CLIENTS", "Dupont", "Projet", "FACTURE-FACT202609-21.PDF"), "FACT202609-21", "119,30", "09/09/2026");
        var scan = InvoiceCatalog.Scan(_root);
        Assert.Equal(2, scan.Candidates.Count);
        Assert.Equal("FACT202609-21", scan.Candidates[0].Data.InvoiceNumber);
        var second = scan.Candidates[1];
        Assert.Equal(85.31m, second.Data.TotalTtc); Assert.Equal(new DateOnly(2026, 10, 8), second.Data.IssuedOn);
        Assert.Equal("Atelier Martin", second.Data.ClientName); Assert.Equal("Martin", second.FolderClient);
        Assert.Equal(RevenueNature.Services, second.Data.Nature);
        Assert.Empty(scan.Unreadable);
    }

    [Fact]
    public void Scan_ignores_hidden_metadata_other_pdfs_dedupes_copies_and_lists_unreadable_files()
    {
        Invoice(Path.Combine("CLIENTS", "Martin", "Facture-FACT1-1.pdf"), "FACT202610-33", "85,31");
        Invoice(Path.Combine("CLIENTS", "Martin", "Projet", "Sous", "Dossier", "Facture-FACT202610-33.pdf"), "FACT202610-33", "85,31");
        Invoice(Path.Combine(".ms3dprint-manager", "revenues", "pieces", "Facture-FACT202610-99.pdf"), "FACT202610-99", "10,00");
        File.WriteAllText(Path.Combine(_root, "Devis-1.pdf"), "x");
        var broken = Path.Combine(_root, "CLIENTS", "Martin", "Facture-cassee.pdf"); File.WriteAllText(broken, "pas un pdf");
        var scan = InvoiceCatalog.Scan(_root);
        var only = Assert.Single(scan.Candidates);
        Assert.Equal(Path.Combine("CLIENTS", "Martin", "Facture-FACT1-1.pdf"), only.RelativePath);
        Assert.Equal(new[] { Path.Combine("CLIENTS", "Martin", "Facture-cassee.pdf") }, scan.Unreadable);
    }

    [Fact]
    public void Unrecorded_keeps_invoices_missing_from_the_book_and_treats_cancelled_lines_as_missing()
    {
        Invoice(Path.Combine("CLIENTS", "A", "Facture-A.pdf"), "FACT202610-01", "10,00");
        Invoice(Path.Combine("CLIENTS", "A", "Facture-B.pdf"), "FACT202610-02", "20,00");
        Invoice(Path.Combine("CLIENTS", "A", "Facture-C.pdf"), "FACT202610-03", "30,00");
        var scan = InvoiceCatalog.Scan(_root);
        var missing = InvoiceCatalog.Unrecorded(scan.Candidates, [Recorded("fact202610-01"), Recorded("FACT202610-02", cancelled: true)]);
        Assert.Equal(new[] { "FACT202610-02", "FACT202610-03" }, missing.Select(c => c.Data.InvoiceNumber));
    }

    [Fact]
    public void Scan_can_be_cancelled_and_reports_progress()
    {
        for (var i = 1; i <= 3; i++) Invoice(Path.Combine("CLIENTS", "A", $"Facture-{i}.pdf"), $"FACT202610-0{i}", "10,00");
        var seen = 0;
        InvoiceCatalog.Scan(_root, new Progress<int>(_ => { }));
        InvoiceCatalog.Scan(_root, new ImmediateProgress(v => seen = v));
        Assert.Equal(3, seen);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => InvoiceCatalog.Scan(_root, null, cts.Token));
    }

    private sealed class ImmediateProgress(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
}

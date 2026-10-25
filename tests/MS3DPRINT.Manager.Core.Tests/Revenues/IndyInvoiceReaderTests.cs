using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class IndyInvoiceReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-indy-" + Guid.NewGuid().ToString("N"));
    public IndyInvoiceReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private const string Sample = """
        MS3DPRINT   MPO FRANCE
        Facture FACT202610-33   émise le 08/10/2026
        Forfait Fabrication additive 1 54,31
        Total HT 1 285,31
        TVA 0,00
        Total TTC 1 285,31
        Cette facture concerne de la vente de bien et de la prestation de service.
        """;

    [Fact]
    public void Parse_reads_number_date_total_with_thousand_separators_and_mixed_nature()
    {
        var data = IndyInvoiceReader.Parse(Sample, "MPO FRANCE")!;
        Assert.Equal("FACT202610-33", data.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 8), data.IssuedOn);
        Assert.Equal(1285.31m, data.TotalTtc);
        Assert.Equal("MPO FRANCE", data.ClientName);
        Assert.Equal(RevenueNature.Mixed, data.Nature);
    }

    [Theory]
    [InlineData("Facture FACT202601-1 mise le 02/01/2026 Total TTC 10,00 vente de bien", RevenueNature.Goods)]
    [InlineData("Facture FACT202601-1 mise le 02/01/2026 Total TTC 10,00 prestation de service", RevenueNature.Services)]
    public void Parse_infers_a_single_nature_only_from_the_invoice_mention(string text, RevenueNature expected)
        => Assert.Equal(expected, IndyInvoiceReader.Parse(text)!.Nature);

    [Fact]
    public void Parse_reads_down_payment_invoices_and_the_former_invoice_layout()
    {
        var deposit = IndyInvoiceReader.Parse("Facture d'acompte FACT202606-14 émise le 03/06/2026 Total TTC 300,00")!;
        Assert.Equal("FACT202606-14", deposit.InvoiceNumber);
        Assert.Equal(300m, deposit.TotalTtc);
        var former = IndyInvoiceReader.Parse("FACTURE N° 25-10-31\n Le samedi 25 octobre 2025\n Total HT 120,00 TVA ( 20 % ) 24,00 Total TTC 144,00")!;
        Assert.Equal("25-10-31", former.InvoiceNumber);
        Assert.Equal(new DateOnly(2025, 10, 25), former.IssuedOn);
        Assert.Equal(144m, former.TotalTtc);
        Assert.Equal(new DateOnly(2026, 2, 1), IndyInvoiceReader.Parse("FACTURE N° 26-02-01 Le dimanche 1er février 2026 Total TTC 10,00")!.IssuedOn);
    }

    [Fact]
    public void Parse_reads_the_spreadsheet_style_invoice_with_only_a_total_excluding_tax()
    {
        var data = IndyInvoiceReader.Parse("FACTURE\nFAC-2026-006\nDate : 30/07/2026\nÉchéance : 29/08/2026\nSous-total HT 59,12 €\nTOTAL HT 1 059,12 €\nTVA non applicable, art. 293 B du CGI.")!;
        Assert.Equal("FAC-2026-006", data.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 7, 30), data.IssuedOn);
        Assert.Equal(1059.12m, data.TotalTtc);
        Assert.Null(IndyInvoiceReader.Parse("Devis DEV-2026-001 Date : 30/07/2026 TOTAL HT 10,00 €")?.InvoiceNumber);
    }

    [Fact]
    public void Read_finds_the_recipient_under_the_destinataire_heading()
    {
        var pdf = Path.Combine(_root, "plain.pdf");
        var rows = new List<PdfLine>
        {
            new(["FACTURE"]), new(["FAC-2026-006"]), new(["Date : 30/07/2026"]),
            new(["", "", "", "", "", "", "", "DESTINATAIRE"]), new(["", "", "", "", "", "", "", "Atelier Martin"]), new(["", "", "", "", "", "", "", "Averton"]),
            new(["TOTAL HT 59,12 €"]),
        };
        File.WriteAllBytes(pdf, SimplePdfWriter.Write("Maxime", [], new PdfLine(["x"]), rows, [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]));
        var data = IndyInvoiceReader.Read(pdf)!;
        Assert.Equal("FAC-2026-006", data.InvoiceNumber); Assert.Equal("Atelier Martin", data.ClientName); Assert.Equal(59.12m, data.TotalTtc);
    }

    [Fact]
    public void Parse_returns_null_when_nothing_looks_like_an_invoice_and_keeps_partial_data_otherwise()
    {
        Assert.Null(IndyInvoiceReader.Parse("Relevé de compte sans rapport"));
        var partial = IndyInvoiceReader.Parse("Facture FACT202601-7 sans total lisible")!;
        Assert.Equal("FACT202601-7", partial.InvoiceNumber);
        Assert.Null(partial.TotalTtc);
        Assert.Null(partial.IssuedOn);
        Assert.Null(partial.Nature);
    }

    [Fact]
    public void Read_extracts_the_fields_and_the_client_block_from_a_real_pdf()
    {
        var pdf = Path.Combine(_root, "facture.pdf");
        var rows = new List<PdfLine>
        {
            new(["", "", "", "", "", "", "", "MPO FRANCE"]),
            new(["", "", "", "", "", "", "", "Lorgerie"]),
            new(["Facture FACT202610-33"]),
            new(["émise le 08/10/2026"]),
            new(["Total TTC 85,31"]),
            new(["Cette facture concerne de la vente de bien"]),
            new(["et de la prestation de service."]),
        };
        File.WriteAllBytes(pdf, SimplePdfWriter.Write("MS3DPRINT", [], new PdfLine(["x"]), rows,
            [0, 52, 160, 285, 345, 410, 475, 540, 612, 690], [60, 10, 10, 10, 10, 10, 10, 30, 10, 10]));
        var data = IndyInvoiceReader.Read(pdf)!;
        Assert.Equal("FACT202610-33", data.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 8), data.IssuedOn);
        Assert.Equal(85.31m, data.TotalTtc);
        Assert.Equal("MPO FRANCE", data.ClientName);
        Assert.Equal(RevenueNature.Mixed, data.Nature);
    }

    [Fact]
    public void Read_returns_null_for_missing_empty_or_non_pdf_files()
    {
        Assert.Null(IndyInvoiceReader.Read(Path.Combine(_root, "absent.pdf")));
        var junk = Path.Combine(_root, "junk.pdf"); File.WriteAllText(junk, "pas un pdf");
        Assert.Null(IndyInvoiceReader.Read(junk));
        var empty = Path.Combine(_root, "empty.pdf"); File.WriteAllBytes(empty, []);
        Assert.Null(IndyInvoiceReader.Read(empty));
    }
}

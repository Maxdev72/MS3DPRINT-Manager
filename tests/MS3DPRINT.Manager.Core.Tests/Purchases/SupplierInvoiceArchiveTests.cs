using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Purchases;

public sealed class SupplierInvoiceArchiveTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ms3d-supplier-invoices-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_base, "workspace");
    public SupplierInvoiceArchiveTests() => Directory.CreateDirectory(Root);
    public void Dispose() => Directory.Delete(_base, true);
    private string Source(string content = "pdf", string name = "facture.pdf")
    {
        var folder = Path.Combine(_base, "source"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name); File.WriteAllText(path, content); return path;
    }

    [Fact]
    public void Purchase_is_copied_into_the_supplier_invoice_year_and_original_is_kept()
    {
        var source = Source();
        var relative = RevenueInvoiceArchive.StorePurchase(Root, source, "Bambu Lab", 2026);
        Assert.Equal(Path.Combine("06_FOURNISSEURS", "BAMBU_LAB", "04_FACTURES", "2026", "facture.pdf"), relative);
        Assert.Equal("pdf", File.ReadAllText(RevenueInvoiceArchive.Resolve(Root, relative)!));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Existing_supplier_folder_is_reused_despite_case_accents_and_spaces()
    {
        var existing = Path.Combine(Root, "06_FOURNISSEURS", "Atelier Résine"); Directory.CreateDirectory(existing);
        var relative = RevenueInvoiceArchive.StorePurchase(Root, Source(), "ATELIER_RESINE", 2025);
        Assert.Equal(Path.Combine("06_FOURNISSEURS", "Atelier Résine", "04_FACTURES", "2025", "facture.pdf"), relative);
        Assert.Single(Directory.GetDirectories(Path.Combine(Root, "06_FOURNISSEURS")));
    }

    [Fact]
    public void Images_are_also_filed_and_different_years_remain_separate()
    {
        var source = Source("photo", "ticket.jpg");
        var first = RevenueInvoiceArchive.StorePurchase(Root, source, "Amazon", 2025);
        var second = RevenueInvoiceArchive.StorePurchase(Root, source, "Amazon", 2026);
        Assert.Contains(Path.Combine("04_FACTURES", "2025"), first);
        Assert.Contains(Path.Combine("04_FACTURES", "2026"), second);
        Assert.True(File.Exists(Path.Combine(Root, first))); Assert.True(File.Exists(Path.Combine(Root, second)));
    }

    [Fact]
    public void Identical_receipt_is_reused_and_name_conflicts_never_overwrite()
    {
        var first = RevenueInvoiceArchive.StorePurchase(Root, Source("A"), "Amazon", 2026);
        Assert.Equal(first, RevenueInvoiceArchive.StorePurchase(Root, Source("A"), "AMAZON", 2026));
        var second = RevenueInvoiceArchive.StorePurchase(Root, Source("B"), "Amazon", 2026);
        Assert.EndsWith("facture (2).pdf", second);
        Assert.Equal("A", File.ReadAllText(Path.Combine(Root, first)));
        Assert.Equal("B", File.ReadAllText(Path.Combine(Root, second)));
    }

    [Fact]
    public void Receipt_copy_keeps_content_without_inheriting_source_storage_attributes()
    {
        var source = Source(); File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            var relative = RevenueInvoiceArchive.StorePurchase(Root, source, "Amazon", 2026);
            var copied = Path.Combine(Root, relative);
            Assert.Equal("pdf", File.ReadAllText(copied));
            Assert.False(File.GetAttributes(copied).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Ambiguous_supplier_folders_are_reported_instead_of_choosing_one()
    {
        Directory.CreateDirectory(Path.Combine(Root, "06_FOURNISSEURS", "Atelier Résine"));
        Directory.CreateDirectory(Path.Combine(Root, "06_FOURNISSEURS", "ATELIER_RESINE"));
        Assert.Throws<InvalidOperationException>(() => RevenueInvoiceArchive.StorePurchase(Root, Source(), "Atelier Resine", 2026));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../..")]
    public void Empty_or_unusable_supplier_is_rejected(string supplier)
        => Assert.Throws<ArgumentException>(() => RevenueInvoiceArchive.StorePurchase(Root, Source(), supplier, 2026));
}

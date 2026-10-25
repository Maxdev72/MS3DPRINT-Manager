using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class RevenueInvoiceArchiveTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ms3d-archive-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_base, "ws");
    private string Source(string content = "pdf-1", string name = "Facture-FACT1.pdf")
    {
        var folder = Path.Combine(_base, "downloads"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name); File.WriteAllText(path, content); return path;
    }
    public RevenueInvoiceArchiveTests() => Directory.CreateDirectory(Root);
    public void Dispose() { if (Directory.Exists(_base)) Directory.Delete(_base, true); }

    [Fact]
    public void Store_without_client_goes_to_the_year_pieces_folder_and_leaves_the_original()
    {
        var source = Source();
        var relative = RevenueInvoiceArchive.Store(Root, source, null, 2026);
        Assert.Equal(Path.Combine(".ms3dprint-manager", "revenues", "pieces", "2026", "Facture-FACT1.pdf"), relative);
        Assert.True(File.Exists(Path.Combine(Root, relative)));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Store_with_a_client_folder_uses_its_Documents_folder()
    {
        var client = Path.Combine(Root, "CLIENTS", "Dupont"); Directory.CreateDirectory(client);
        var relative = RevenueInvoiceArchive.Store(Root, Source(), client, 2026);
        Assert.Equal(Path.Combine("CLIENTS", "Dupont", "Documents", "Facture-FACT1.pdf"), relative);
    }

    [Fact]
    public void A_client_folder_outside_the_workspace_is_ignored()
    {
        var outside = Path.Combine(_base, "ailleurs"); Directory.CreateDirectory(outside);
        var relative = RevenueInvoiceArchive.Store(Root, Source(), outside, 2026);
        Assert.StartsWith(".ms3dprint-manager", relative);
        Assert.False(Directory.Exists(Path.Combine(outside, "Documents")));
    }

    [Fact]
    public void Identical_content_is_reused_and_different_content_gets_a_new_name_without_overwriting()
    {
        var first = RevenueInvoiceArchive.Store(Root, Source("A"), null, 2026);
        Assert.Equal(first, RevenueInvoiceArchive.Store(Root, Source("A"), null, 2026));
        var second = RevenueInvoiceArchive.Store(Root, Source("B"), null, 2026);
        Assert.EndsWith("Facture-FACT1 (2).pdf", second);
        Assert.Equal("A", File.ReadAllText(Path.Combine(Root, first)));
        Assert.Equal("B", File.ReadAllText(Path.Combine(Root, second)));
    }

    [Fact]
    public void Resolve_returns_existing_files_inside_the_workspace_only()
    {
        var relative = RevenueInvoiceArchive.Store(Root, Source(), null, 2026);
        Assert.Equal(Path.Combine(Root, relative), RevenueInvoiceArchive.Resolve(Root, relative));
        Assert.Null(RevenueInvoiceArchive.Resolve(Root, Path.Combine("..", "downloads", "Facture-FACT1.pdf")));
        Assert.Null(RevenueInvoiceArchive.Resolve(Root, Source()));
        Assert.Null(RevenueInvoiceArchive.Resolve(Root, "absent.pdf"));
        Assert.Null(RevenueInvoiceArchive.Resolve(Root, null));
    }
}

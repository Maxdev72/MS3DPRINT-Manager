using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class InvoiceImportDeduplicationTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-invoice-dedup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static RevenueEntry Entry(string number = "FACT202610-51", int year = 2026) => new(Guid.NewGuid(), new DateOnly(year, 10, 8),
        "Facture " + number, 40m, RevenueNature.Services, 0, 40m, "Virement", null, "Client", null, number, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Automatic_invoice_import_rechecks_existing_numbers_and_does_not_append_duplicates()
    {
        var journal = new RevenueJournal(_root);
        Assert.True(journal.TryAppendInvoice(Entry(), Today));
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl"); var before = File.ReadAllBytes(path);
        Assert.False(journal.TryAppendInvoice(Entry(" fact202610-51 "), Today));
        Assert.Single(journal.Load(2026).Lines); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void An_invoice_already_collected_in_another_year_is_not_imported_again()
    {
        var journal = new RevenueJournal(_root); journal.Append(Entry(year: 2025), Today);
        Assert.False(journal.TryAppendInvoice(Entry(), Today));
        Assert.Empty(journal.Load(2026).Lines); Assert.Single(journal.Load(2025).Lines);
    }

    [Fact]
    public void A_cancelled_collection_can_be_replaced_but_a_corrected_reference_is_current()
    {
        var journal = new RevenueJournal(_root); var cancelled = Entry(); journal.Append(cancelled, Today);
        journal.Cancel(cancelled, "Erreur", DateTimeOffset.UtcNow);
        var replacement = Entry(); Assert.True(journal.TryAppendInvoice(replacement, Today));
        journal.Correct(replacement, replacement with { InvoiceNumber = "FACT202610-52" }, "Référence corrigée", DateTimeOffset.UtcNow, Today);
        Assert.False(journal.TryAppendInvoice(Entry("FACT202610-52"), Today));
        Assert.True(journal.TryAppendInvoice(Entry(), Today));
        Assert.Equal(2, journal.Load(2026).Lines.Count(l => !l.IsCancelled));
    }

    [Fact]
    public async Task Concurrent_local_imports_of_the_same_invoice_in_different_years_record_it_only_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            new RevenueJournal(_root).TryAppendInvoice(Entry(year: index % 2 == 0 ? 2025 : 2026), Today))));
        Assert.Single(results.Where(added => added));
        var journal = new RevenueJournal(_root);
        Assert.Single(journal.Years().SelectMany(year => journal.Load(year).Lines));
    }

    [Fact]
    public void An_unreadable_book_blocks_automatic_import_without_changing_the_file()
    {
        var journal = new RevenueJournal(_root); journal.Append(Entry(), Today);
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl");
        File.AppendAllText(path, "{ligne illisible}"); var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => journal.TryAppendInvoice(Entry("FACT202610-52"), Today));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Automatic_import_preserves_the_mixed_split_and_manual_partial_collections_remain_possible()
    {
        var journal = new RevenueJournal(_root);
        var mixed = Entry() with { Nature = RevenueNature.Mixed, GoodsAmount = 24.99m, ServicesAmount = 15.01m };
        Assert.True(journal.TryAppendInvoice(mixed, Today)); Assert.False(journal.TryAppendInvoice(mixed with { Id = Guid.NewGuid() }, Today));
        Assert.Equal(mixed, Assert.Single(journal.Load(2026).Lines).Entry);
        journal.Append(Entry() with { Amount = 10m, GoodsAmount = 0, ServicesAmount = 10m }, Today);
        Assert.Equal(2, journal.Load(2026).Lines.Count);
    }
}

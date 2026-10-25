using System.Text;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Purchases;

public sealed class PurchaseRegisterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-purchases-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 10, 25);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static PurchaseEntry Entry(string reference = "A-1", decimal amount = 25.9m, DateOnly? on = null, string supplier = "Bambu Lab") =>
        new(Guid.NewGuid(), on ?? new DateOnly(2026, 10, 20), supplier, "Bobines PLA", amount, "Carte bancaire", reference, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Append_then_load_keeps_the_entry_in_the_file_of_its_year()
    {
        var journal = new PurchaseJournal(_root);
        var entry = Entry();
        journal.Append(entry, Today);
        journal.Append(Entry("A-0", on: new DateOnly(2025, 12, 30)), Today);
        Assert.Equal(new[] { 2026, 2025 }, journal.Years());
        Assert.Equal(entry.Id, Assert.Single(journal.Load(2026).Lines).Entry.Id);
        Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "purchases", "2026.jsonl")));
    }

    [Fact]
    public void Invalid_entries_are_refused()
    {
        var journal = new PurchaseJournal(_root);
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Append(Entry(amount: 0), Today));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Append(Entry(on: Today.AddDays(1)), Today));
        Assert.Throws<ArgumentException>(() => journal.Append(Entry() with { Supplier = " " }, Today));
        Assert.Throws<ArgumentException>(() => journal.Append(Entry() with { Label = "" }, Today));
        Assert.Throws<ArgumentException>(() => journal.Append(Entry(reference: " "), Today));
    }

    [Fact]
    public void Cancellation_needs_a_reason_keeps_the_line_and_cannot_repeat()
    {
        var journal = new PurchaseJournal(_root);
        var entry = Entry();
        journal.Append(entry, Today);
        Assert.Throws<ArgumentException>(() => journal.Cancel(entry, " ", DateTimeOffset.UtcNow));
        journal.Cancel(entry, "Doublon", DateTimeOffset.UtcNow);
        var line = Assert.Single(journal.Load(2026).Lines);
        Assert.True(line.IsCancelled); Assert.Equal("Doublon", line.Cancellation!.Reason);
        Assert.Throws<InvalidOperationException>(() => journal.Cancel(entry, "Encore", DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => journal.Cancel(Entry(), "Absent", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Unreadable_lines_are_reported_and_the_file_is_never_rewritten()
    {
        var journal = new PurchaseJournal(_root);
        journal.Append(Entry("A-1"), Today);
        var path = Path.Combine(_root, ".ms3dprint-manager", "purchases", "2026.jsonl");
        File.AppendAllText(path, "{pas du json");
        var before = File.ReadAllText(path);
        journal.Append(Entry("A-2"), Today);
        var year = journal.Load(2026);
        Assert.Equal(2, year.Lines.Count); Assert.Single(year.Errors);
        Assert.StartsWith(before.TrimEnd('\r', '\n'), File.ReadAllText(path));
    }

    [Fact]
    public void Same_supplier_and_reference_is_detected_but_not_another_supplier()
    {
        var journal = new PurchaseJournal(_root);
        journal.Append(Entry("A-1"), Today);
        Assert.True(journal.IsReferenceKnown("bambu lab", "a-1"));
        Assert.False(journal.IsReferenceKnown("Autre", "A-1"));
    }

    [Fact]
    public void Totals_by_month_ignore_cancelled_purchases()
    {
        var cancelled = Entry("A-2", 99m);
        var summary = PurchaseTotals.Compute([new PurchaseLine(Entry("A-1", 10m, new DateOnly(2026, 3, 1)), null), new PurchaseLine(Entry("A-3", 5.5m, new DateOnly(2026, 3, 9)), null),
            new PurchaseLine(cancelled, new PurchaseCancellation(Guid.NewGuid(), cancelled.Id, "x", DateTimeOffset.UtcNow))]);
        Assert.Equal(15.5m, summary.Total); Assert.Equal(15.5m, summary.Months[2]); Assert.Equal(0m, summary.Months[0]); Assert.Equal(1, summary.CancelledCount);
    }

    [Fact]
    public void Csv_and_pdf_exports_contain_the_data_and_protect_against_formulas()
    {
        var line = new PurchaseLine(Entry("A-1", 1234.5m, supplier: "=SOMME(1;2)"), null);
        var year = new PurchaseYear(2026, [line], []);
        var csv = PurchaseExporter.ToCsv(year);
        Assert.Contains("Fournisseur", csv); Assert.Contains(";\"'=SOMME", csv); Assert.Contains(";1234,50;", csv);
        var pdf = Encoding.Latin1.GetString(PurchaseExporter.ToPdf(year, new RevenueHeader("MS3DPRINT", "123")));
        Assert.StartsWith("%PDF-1.4", pdf); Assert.Contains("Registre des achats 2026", pdf); Assert.Contains("SIRET 123", pdf); Assert.Contains("TOTAL 2026", pdf);
    }

    [Fact]
    public void Documents_of_purchases_are_stored_in_their_own_pieces_folder()
    {
        var source = Path.Combine(_root, "src.pdf"); Directory.CreateDirectory(_root); File.WriteAllText(source, "x");
        var relative = RevenueInvoiceArchive.Store(_root, source, null, 2026, "purchases");
        Assert.Equal(Path.Combine(".ms3dprint-manager", "purchases", "pieces", "2026", "src.pdf"), relative);
    }
}

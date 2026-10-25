using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class RevenueJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-revenues-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 10, 25);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static RevenueEntry Entry(string invoice = "F-001", decimal amount = 120m, DateOnly? on = null, RevenueNature nature = RevenueNature.Services) =>
        new(Guid.NewGuid(), on ?? new DateOnly(2026, 10, 20), "Impression de prototypes", amount, nature,
            nature == RevenueNature.Goods ? amount : nature == RevenueNature.Services ? 0 : 40m,
            nature == RevenueNature.Services ? amount : nature == RevenueNature.Goods ? 0 : amount - 40m,
            "Virement", null, "Client A", "P-001", invoice, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Append_then_load_returns_the_entry_in_the_year_file()
    {
        var journal = new RevenueJournal(_root);
        var entry = Entry();
        journal.Append(entry, Today);
        var year = journal.Load(2026);
        var line = Assert.Single(year.Lines);
        Assert.Equal(entry.Id, line.Entry.Id);
        Assert.False(line.IsCancelled);
        Assert.Empty(year.Errors);
        Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl")));
    }

    [Fact]
    public void Entries_go_to_the_file_of_their_collection_year_and_years_are_listed()
    {
        var journal = new RevenueJournal(_root);
        journal.Append(Entry("F-1", on: new DateOnly(2025, 12, 31)), Today);
        journal.Append(Entry("F-2", on: new DateOnly(2026, 1, 2)), Today);
        Assert.Equal(new[] { 2026, 2025 }, journal.Years());
        Assert.Single(journal.Load(2025).Lines);
        Assert.Single(journal.Load(2026).Lines);
    }

    [Fact]
    public void Lines_are_sorted_chronologically()
    {
        var journal = new RevenueJournal(_root);
        journal.Append(Entry("F-2", on: new DateOnly(2026, 5, 2)), Today);
        journal.Append(Entry("F-1", on: new DateOnly(2026, 2, 1)), Today);
        Assert.Equal(new[] { "F-1", "F-2" }, journal.Load(2026).Lines.Select(l => l.Entry.InvoiceNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amounts_are_refused(int amount)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RevenueJournal(_root).Append(Entry(amount: amount), Today));

    [Fact]
    public void A_future_collection_date_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RevenueJournal(_root).Append(Entry(on: Today.AddDays(1)), Today));

    [Fact]
    public void Label_and_invoice_reference_are_required()
    {
        var journal = new RevenueJournal(_root);
        Assert.Throws<ArgumentException>(() => journal.Append(Entry() with { Label = " " }, Today));
        Assert.Throws<ArgumentException>(() => journal.Append(Entry() with { InvoiceNumber = "" }, Today));
    }

    [Fact]
    public void A_mixed_entry_must_split_exactly_the_total()
    {
        var journal = new RevenueJournal(_root);
        Assert.Throws<ArgumentException>(() => journal.Append(Entry(nature: RevenueNature.Mixed) with { GoodsAmount = 10m }, Today));
        journal.Append(Entry(nature: RevenueNature.Mixed), Today);
    }

    [Fact]
    public void Cancel_keeps_the_entry_visible_with_its_reason_and_cannot_repeat()
    {
        var journal = new RevenueJournal(_root);
        var entry = Entry();
        journal.Append(entry, Today);
        journal.Cancel(entry, "Montant erroné", DateTimeOffset.UtcNow);
        var line = Assert.Single(journal.Load(2026).Lines);
        Assert.True(line.IsCancelled);
        Assert.Equal("Montant erroné", line.Cancellation!.Reason);
        Assert.Throws<InvalidOperationException>(() => journal.Cancel(entry, "Encore", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => journal.Cancel(Entry(), "  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancelling_an_unknown_entry_fails()
        => Assert.Throws<InvalidOperationException>(() => new RevenueJournal(_root).Cancel(Entry(), "Absente", DateTimeOffset.UtcNow));

    [Fact]
    public void Unreadable_lines_are_reported_without_hiding_the_others_and_the_file_is_never_rewritten()
    {
        var journal = new RevenueJournal(_root);
        journal.Append(Entry("F-1"), Today);
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl");
        File.AppendAllText(path, "{ceci n'est pas du json");
        var before = File.ReadAllText(path);
        journal.Append(Entry("F-2"), Today);
        var year = journal.Load(2026);
        Assert.Equal(2, year.Lines.Count);
        Assert.Single(year.Errors);
        Assert.StartsWith(before.TrimEnd('\r', '\n'), File.ReadAllText(path));
    }

    [Fact]
    public void Missing_year_is_empty_and_duplicate_invoices_are_detected_across_years()
    {
        var journal = new RevenueJournal(_root);
        Assert.Empty(journal.Load(2020).Lines);
        journal.Append(Entry("F-9", on: new DateOnly(2025, 3, 3)), Today);
        Assert.True(journal.IsInvoiceKnown("f-9"));
        Assert.False(journal.IsInvoiceKnown("F-10"));
    }
}

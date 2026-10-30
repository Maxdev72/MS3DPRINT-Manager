using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class InvoiceReceivablesTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-receivables-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static InvoiceCandidate Invoice(string number = "F-001", decimal total = 40m) => new("facture.pdf", "CLIENTS/Client/facture.pdf",
        new(number, new DateOnly(2026, 10, 1), total, "Client", RevenueNature.Services), "Client");
    private static RevenueEntry Payment(decimal amount, string number = "F-001", int year = 2026) => new(Guid.NewGuid(), new DateOnly(year, 10, 10),
        "Paiement " + number, amount, RevenueNature.Services, 0, amount, "Virement", null, "Client", null, number, null, DateTimeOffset.UtcNow);
    private static RevenueLine Line(RevenueEntry entry) => new(entry, null);

    [Fact]
    public void An_issued_invoice_without_a_payment_remains_unpaid_and_does_not_create_revenue()
    {
        var journal = new RevenueJournal(_root);
        var item = Assert.Single(InvoiceReceivables.Build([Invoice()], []));
        Assert.Equal(InvoicePaymentState.Unpaid, item.State); Assert.Equal(40m, item.Remaining); Assert.Equal(0m, item.Collected);
        Assert.Empty(journal.Years());
    }

    [Fact]
    public void Active_collections_match_across_years_and_use_current_corrected_values()
    {
        var cancelled = Payment(100m);
        var corrected = Payment(15m);
        var lines = new RevenueLine[]
        {
            Line(Payment(10m, " f-001 ", 2025)), Line(corrected),
            new(cancelled, new(Guid.NewGuid(), cancelled.Id, "Erreur", DateTimeOffset.UtcNow)), Line(Payment(999m, "OTHER"))
        };
        var item = Assert.Single(InvoiceReceivables.Build([Invoice()], lines));
        Assert.Equal(InvoicePaymentState.Partial, item.State); Assert.Equal(25m, item.Collected); Assert.Equal(15m, item.Remaining);
        Assert.Equal(2, item.Payments.Count);
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(50, 10)]
    public void Paid_invoices_have_no_remaining_balance_and_overpayments_are_visible(int collected, int overpaid)
    {
        var item = Assert.Single(InvoiceReceivables.Build([Invoice()], [Line(Payment(collected))]));
        Assert.Equal(InvoicePaymentState.Paid, item.State); Assert.Equal(0m, item.Remaining); Assert.Equal(overpaid, item.Overpaid);
    }

    [Fact]
    public void Copies_of_the_same_invoice_do_not_duplicate_the_receivable()
        => Assert.Single(InvoiceReceivables.Build([Invoice(), Invoice(" f-001 ")], []));

    [Fact]
    public void Partial_payments_progress_to_paid_and_cancellation_reopens_the_invoice()
    {
        var journal = new RevenueJournal(_root); var first = Payment(10m); var second = Payment(30m);
        journal.AppendInvoicePayment(first, 40m, 0m, Today);
        Assert.Equal(InvoicePaymentState.Partial, Assert.Single(InvoiceReceivables.Build([Invoice()], journal.Load(2026).Lines)).State);
        journal.AppendInvoicePayment(second, 40m, 10m, Today);
        Assert.Equal(InvoicePaymentState.Paid, Assert.Single(InvoiceReceivables.Build([Invoice()], journal.Load(2026).Lines)).State);
        journal.Cancel(second, "Paiement erroné", DateTimeOffset.UtcNow);
        Assert.Equal(30m, Assert.Single(InvoiceReceivables.Build([Invoice()], journal.Load(2026).Lines)).Remaining);
    }

    [Fact]
    public void A_payment_above_the_balance_or_from_a_stale_form_is_refused_without_writing()
    {
        var journal = new RevenueJournal(_root); journal.Append(Payment(10m), Today);
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl"); var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => journal.AppendInvoicePayment(Payment(31m), 40m, 10m, Today));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Throws<InvalidOperationException>(() => journal.AppendInvoicePayment(Payment(10m), 40m, 0m, Today));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_corrected_collection_changes_the_actual_balance_before_the_next_payment()
    {
        var journal = new RevenueJournal(_root); var original = Payment(10m); journal.Append(original, Today);
        journal.Correct(original, original with { Amount = 5m, ServicesAmount = 5m }, "Montant corrigé", DateTimeOffset.UtcNow, Today);
        journal.AppendInvoicePayment(Payment(35m), 40m, 5m, Today);
        Assert.Equal(0m, Assert.Single(InvoiceReceivables.Build([Invoice()], journal.Load(2026).Lines)).Remaining);
    }

    [Fact]
    public async Task Concurrent_payments_based_on_the_same_balance_require_a_refresh_before_the_second()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() =>
        {
            try { new RevenueJournal(_root).AppendInvoicePayment(Payment(20m, year: index == 0 ? 2025 : 2026), 40m, 0m, Today); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results.Where(added => added));
        var journal = new RevenueJournal(_root); Assert.Single(journal.Years().SelectMany(year => journal.Load(year).Lines));
    }

    [Fact]
    public void An_unreadable_book_refuses_invoice_payments()
    {
        var journal = new RevenueJournal(_root); journal.Append(Payment(10m), Today);
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl"); File.AppendAllText(path, "{illisible}");
        Assert.Throws<InvalidDataException>(() => journal.AppendInvoicePayment(Payment(10m), 40m, 10m, Today));
    }
}

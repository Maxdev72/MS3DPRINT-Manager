namespace MS3DPRINT.Manager.Core.Revenues;

public enum InvoicePaymentState { Unpaid, Partial, Paid }

public sealed record InvoiceReceivable(InvoiceCandidate Invoice, IReadOnlyList<RevenueEntry> Payments)
{
    public decimal Total => Invoice.Data.TotalTtc!.Value;
    public decimal Collected => Payments.Sum(payment => payment.Amount);
    public decimal Remaining => Math.Max(0, Total - Collected);
    public decimal Overpaid => Math.Max(0, Collected - Total);
    public InvoicePaymentState State => Collected == 0 ? InvoicePaymentState.Unpaid : Remaining > 0 ? InvoicePaymentState.Partial : InvoicePaymentState.Paid;
}

public static class InvoiceReceivables
{
    public static IReadOnlyList<InvoiceReceivable> Build(IEnumerable<InvoiceCandidate> invoices, IEnumerable<RevenueLine> lines)
    {
        ArgumentNullException.ThrowIfNull(invoices); ArgumentNullException.ThrowIfNull(lines);
        var payments = lines.Where(line => !line.IsCancelled).Select(line => line.Entry)
            .ToLookup(entry => entry.InvoiceNumber.Trim(), StringComparer.OrdinalIgnoreCase);
        return invoices.Where(invoice => !string.IsNullOrWhiteSpace(invoice.Data.InvoiceNumber) && invoice.Data.TotalTtc is > 0)
            .GroupBy(invoice => invoice.Data.InvoiceNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new InvoiceReceivable(group.First(), payments[group.Key].OrderBy(entry => entry.ReceivedOn).ThenBy(entry => entry.RecordedAt).ToArray()))
            .OrderBy(item => item.Invoice.Data.IssuedOn ?? DateOnly.MinValue).ThenBy(item => item.Invoice.Data.InvoiceNumber, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

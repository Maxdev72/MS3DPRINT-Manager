using Avalonia.Interactivity;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _invoiceTrackingOpen;

    private async void TrackInvoices_Click(object? sender, RoutedEventArgs args)
    {
        if (_invoiceTrackingOpen) return;
        _invoiceTrackingOpen = true;
        try { await ShowInvoiceTrackingAsync(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify("Suivi des factures indisponible : " + error.Message, error: true); }
        finally { _invoiceTrackingOpen = false; }
    }

    private async Task ShowInvoiceTrackingAsync()
    {
        var root = WorkspaceRoot; var navigationKey = CurrentNavigationStop?.Key;
        var filter = InvoiceTrackingFilter.Outstanding; var search = "";
        InvoiceScan? scan = null;
        bool Current() => IsVisible && RevenuesWorkspacePanel.IsVisible && MatchesNavigationContext(root, navigationKey);
        while (Current())
        {
            InvoiceScan found; IReadOnlyList<InvoiceReceivable> invoices;
            var cached = scan;
            BeginBusy();
            try
            {
                (found, invoices) = await Task.Run(() =>
                {
                    var documents = cached ?? InvoiceCatalog.Scan(root);
                    var lines = new RevenueJournal(root).LoadInvoiceLines();
                    return (documents, InvoiceReceivables.Build(documents.Candidates, lines));
                });
            }
            finally { EndBusy(); }
            if (!Current()) return;
            scan = found;
            var dialog = new InvoiceTrackingWindow(invoices, found.Unreadable.Count + found.Candidates.Count - invoices.Count, found.Truncated, filter, search);
            var accepted = await dialog.ShowDialog<bool>(this);
            filter = dialog.Filter; search = dialog.SearchText;
            if (!accepted || !Current()) return;
            if (dialog.RefreshRequested) { scan = null; continue; }
            if (dialog.ImportRequested) { await ImportInvoicesAsync(); continue; }
            if (dialog.PaymentRequested is not { } invoice) return;
            var payment = new InvoicePaymentWindow(invoice, DateOnly.FromDateTime(DateTime.Today));
            if (!await payment.ShowDialog<bool>(this) || payment.Result is not { } entry) continue;
            try
            {
                await Task.Run(() => new RevenueJournal(root).AppendInvoicePayment(entry, invoice.Total, invoice.Collected));
                _revenueYear = entry.ReceivedOn.Year;
                Notify("Paiement enregistré. " + (entry.Amount == invoice.Remaining ? "Facture soldée." : $"Reste à encaisser : {RevenueExporter.Money(invoice.Remaining - entry.Amount)}."));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { Notify("Paiement non enregistré : " + error.Message, error: true); }
            if (Current()) await LoadRevenuesAsync();
        }
    }
}

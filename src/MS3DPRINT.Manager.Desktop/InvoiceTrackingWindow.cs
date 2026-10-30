using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public enum InvoiceTrackingFilter { Outstanding, Partial, Paid, All }

public sealed class InvoiceTrackingWindow : DialogWindow
{
    private readonly IReadOnlyList<InvoiceReceivable> _invoices;
    private readonly TextBox _search = new() { PlaceholderText = "Numéro de facture ou client…" };
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } };
    private readonly List<(Button Button, InvoiceTrackingFilter Filter)> _filters = [];
    public string SearchText => _search.Text ?? "";
    public InvoiceTrackingFilter Filter { get; private set; }
    public InvoiceReceivable? PaymentRequested { get; private set; }
    public bool RefreshRequested { get; private set; }
    public bool ImportRequested { get; private set; }
    public InvoiceTrackingWindow(IReadOnlyList<InvoiceReceivable> invoices, int unreadable = 0, bool truncated = false,
        InvoiceTrackingFilter filter = InvoiceTrackingFilter.Outstanding, string search = "")
    {
        _invoices = invoices; Filter = filter;
        Title = "Suivi des factures"; Width = 640; Height = 800; MinWidth = 440;
        var close = new Button { Content = "Fermer" }; close.Click += (_, _) => Close(false);
        var refresh = new Button { Content = "Actualiser", Classes = { "ghost" } };
        refresh.Click += (_, _) => { RefreshRequested = true; Close(true); };
        var import = new Button { Content = "Importer plusieurs encaissements", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += (_, _) => { ImportRequested = true; Close(true); };
        var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var choice in new[] { ("À encaisser", InvoiceTrackingFilter.Outstanding), ("Partielles", InvoiceTrackingFilter.Partial), ("Soldées", InvoiceTrackingFilter.Paid), ("Toutes", InvoiceTrackingFilter.All) })
        {
            var button = new Button { Content = choice.Item1, Classes = { "filter-chip" } };
            button.Click += (_, _) => { Filter = choice.Item2; Render(); };
            _filters.Add((button, choice.Item2)); filters.Children.Add(button);
        }
        _search.Text = search;
        Avalonia.Automation.AutomationProperties.SetName(_search, "Rechercher une facture");
        _search.TextChanged += (_, _) => Render();
        _search.KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) args.Handled = true; };
        var warning = new TextBlock { IsVisible = unreadable > 0 || truncated, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = (unreadable > 0 ? $"{unreadable} PDF non exploitable(s) pour le suivi. " : "") + (truncated ? "La recherche a atteint sa limite : certaines factures peuvent manquer." : "") };
        Layout("Suivi des factures", "PDF classés et paiements de toutes les années. Une facture sans paiement ne crée aucune recette.",
            [_search, filters, _summary, warning, import, _rows], close, refresh);
        Render();
    }

    private void Render()
    {
        foreach (var choice in _filters) choice.Button.Classes.Set("selected", choice.Filter == Filter);
        var query = SearchText.Trim();
        var invoices = _invoices.Where(invoice => Filter switch
        {
            InvoiceTrackingFilter.Outstanding => invoice.Remaining > 0,
            InvoiceTrackingFilter.Partial => invoice.State == InvoicePaymentState.Partial,
            InvoiceTrackingFilter.Paid => invoice.State == InvoicePaymentState.Paid,
            _ => true
        }).Where(invoice => query.Length == 0 || $"{invoice.Invoice.Data.InvoiceNumber} {invoice.Invoice.Data.ClientName} {invoice.Invoice.FolderClient}".Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _summary.Text = $"{invoices.Length} facture{(invoices.Length > 1 ? "s" : "")} affichée{(invoices.Length > 1 ? "s" : "")} · Reste à encaisser : {RevenueExporter.Money(invoices.Sum(invoice => invoice.Remaining))}";
        _rows.Children.Clear();
        foreach (var invoice in invoices) _rows.Children.Add(Row(invoice));
        if (invoices.Length == 0) _rows.Children.Add(new TextBlock { Text = "Aucune facture pour ce filtre.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } });
    }

    private Control Row(InvoiceReceivable invoice)
    {
        var data = invoice.Invoice.Data;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        heading.Children.Add(new TextBlock { Text = $"{data.InvoiceNumber} · {data.ClientName ?? invoice.Invoice.FolderClient ?? "Client inconnu"}",
            FontSize = 15, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var label = invoice.State switch { InvoicePaymentState.Unpaid => "À encaisser", InvoicePaymentState.Partial => "Partiellement encaissée", _ => "Soldée" };
        var badge = new Border { Classes = { "badge", invoice.State switch { InvoicePaymentState.Unpaid => "warning", InvoicePaymentState.Partial => "info", _ => "success" } },
            Child = new TextBlock { Text = label } };
        Grid.SetColumn(badge, 1); heading.Children.Add(badge);
        var amounts = new WrapPanel { ItemSpacing = 24, LineSpacing = 10 };
        void Amount(string title, decimal value) => amounts.Children.Add(new StackPanel { Spacing = 4, MinWidth = 130, Children =
        {
            new TextBlock { Text = title, FontSize = 12, Classes = { "muted" } },
            new TextBlock { Text = RevenueExporter.Money(value), FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold }
        } });
        Amount("Facturé", invoice.Total); Amount("Encaissé", invoice.Collected); Amount("Reste à encaisser", invoice.Remaining);
        var error = FormKit.ErrorText();
        var preview = new Button { Content = Icons.Label("IconOpen", "Voir la facture"), Classes = { "ghost" } };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Voir la facture " + data.InvoiceNumber);
        preview.Click += (_, _) => InvoicePreviewUi.Open(invoice.Invoice, preview, error);
        var actions = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Children = { preview } };
        if (invoice.Remaining > 0)
        {
            var payment = new Button { Content = Icons.Label("IconBook", "Enregistrer un paiement"), Classes = { "ghost" } };
            Avalonia.Automation.AutomationProperties.SetName(payment, "Enregistrer un paiement pour " + data.InvoiceNumber);
            payment.Click += (_, _) => { PaymentRequested = invoice; Close(true); };
            actions.Children.Add(payment);
        }
        var content = new StackPanel { Spacing = 12, Children = { heading,
            new TextBlock { Text = "Émise le " + (data.IssuedOn?.ToString("dd/MM/yyyy") ?? "date inconnue"), Classes = { "muted" }, FontSize = 12 }, amounts, actions, error } };
        if (invoice.Overpaid > 0) content.Children.Add(new TextBlock { Text = $"Excédent encaissé : {RevenueExporter.Money(invoice.Overpaid)}. Vérifiez les paiements dans le livre.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } });
        if (invoice.Payments.Count > 0)
        {
            var history = new StackPanel { Spacing = 8 };
            foreach (var payment in invoice.Payments) history.Children.Add(new TextBlock { Text = $"{payment.ReceivedOn:dd/MM/yyyy} · {RevenueExporter.Money(payment.Amount)} · {payment.PaymentMethod}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(new Expander { Header = $"{invoice.Payments.Count} paiement{(invoice.Payments.Count > 1 ? "s" : "")} enregistré{(invoice.Payments.Count > 1 ? "s" : "")}", Content = history });
        }
        return new Border { Classes = { "invoice-tracking-row" }, Padding = new Avalonia.Thickness(0, 16), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = Avalonia.Application.Current?.FindResource("SubtleBorderBrush") as Avalonia.Media.IBrush, Child = content };
    }
}

using Avalonia.Controls;
using System.Globalization;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public sealed class InvoicePaymentWindow : DialogWindow
{
    private readonly InvoiceReceivable _invoice;
    private readonly DateOnly _today;
    private readonly TextBox _date = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _goods = new() { PlaceholderText = "Part du paiement en marchandises" };
    private readonly TextBlock _services = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _nature = new() { ItemsSource = new[] { "Prestation", "Marchandises", "Mixte" }, PlaceholderText = "À choisir" };
    private readonly ComboBox _payment = new() { ItemsSource = RevenueEntryWindow.PaymentMethods, SelectedIndex = 0 };
    private readonly TextBlock _error = FormKit.ErrorText();
    private static readonly RevenueNature[] Natures = [RevenueNature.Services, RevenueNature.Goods, RevenueNature.Mixed];
    public RevenueEntry? Result { get; private set; }
    public InvoicePaymentWindow(InvoiceReceivable invoice, DateOnly today)
    {
        _invoice = invoice; _today = today;
        Title = "Enregistrer un paiement"; Width = 520; Height = 800; MinWidth = 440;
        _date.Text = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _amount.Text = invoice.Remaining.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
        _nature.SelectedIndex = invoice.Invoice.Data.Nature is { } nature ? Array.IndexOf(Natures, nature) : -1;
        var summary = new TextBlock
        {
            Text = $"Facturé : {RevenueExporter.Money(invoice.Total)} · Déjà encaissé : {RevenueExporter.Money(invoice.Collected)}\nReste à encaisser : {RevenueExporter.Money(invoice.Remaining)}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" }
        };
        var preview = new Button { Content = Icons.Label("IconOpen", "Voir la facture"), Classes = { "ghost" } };
        var previewError = FormKit.ErrorText();
        Avalonia.Automation.AutomationProperties.SetName(preview, "Voir la facture " + invoice.Invoice.Data.InvoiceNumber);
        preview.Click += (_, _) => InvoicePreviewUi.Open(invoice.Invoice, preview, previewError);
        var save = new Button { Content = "Enregistrer le paiement" }; save.Click += (_, _) => Save();
        Layout("Enregistrer un paiement", $"{invoice.Invoice.Data.InvoiceNumber} · {invoice.Invoice.Data.ClientName ?? invoice.Invoice.FolderClient ?? "Client inconnu"}",
            [summary, preview, previewError, FormKit.Field("Date d’encaissement", _date, hint: "Date réelle de réception du paiement."),
             FormKit.Field("Montant encaissé", _amount, "€", "Vous pouvez enregistrer une partie du solde."), FormKit.Field("Nature", _nature),
             FormKit.Field("Part marchandises", _goods, "€", "Répartition de ce paiement, pas du total de la facture."),
             FormKit.Field("Part prestations calculée", _services), FormKit.Field("Mode de règlement", _payment), _error], save);
        _nature.SelectionChanged += (_, _) => { FormKit.ClearError(_error); UpdateSplit(); };
        _amount.TextChanged += (_, _) => { FormKit.ClearError(_error); UpdateSplit(); };
        _goods.TextChanged += (_, _) => { FormKit.ClearError(_error); UpdateSplit(); };
        _date.TextChanged += (_, _) => FormKit.ClearError(_error);
        UpdateSplit();
    }

    private void UpdateSplit()
    {
        var mixed = _nature.SelectedIndex >= 0 && Natures[_nature.SelectedIndex] == RevenueNature.Mixed;
        _goods.IsVisible = _services.IsVisible = mixed;
        _services.Text = ReadMoney(_amount.Text, out var amount) && ReadMoney(_goods.Text, out var goods) && goods > 0 && goods < amount
            ? RevenueExporter.Money(amount - goods) : "À calculer";
    }

    private void Save()
    {
        if (!DateOnly.TryParseExact(_date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date > _today)
        { FormKit.ShowError(_error, _date, "Indiquez la date réelle d’encaissement au format jj/mm/aaaa, sans date future."); return; }
        if (!ReadMoney(_amount.Text, out var amount) || amount <= 0 || amount > _invoice.Remaining)
        { FormKit.ShowError(_error, _amount, "Le montant doit être positif et ne pas dépasser le reste à encaisser."); return; }
        if (_nature.SelectedIndex < 0) { FormKit.ShowError(_error, _nature, "Choisissez la nature de ce paiement."); return; }
        var nature = Natures[_nature.SelectedIndex]; decimal goods = nature == RevenueNature.Goods ? amount : 0;
        if (nature == RevenueNature.Mixed && (!ReadMoney(_goods.Text, out goods) || goods <= 0 || goods >= amount))
        { FormKit.ShowError(_error, _goods, "La part marchandises doit être positive et inférieure au montant de ce paiement."); return; }
        var projects = _invoice.Payments.Select(payment => payment.ProjectRef).Where(reference => !string.IsNullOrWhiteSpace(reference)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Result = new RevenueEntry(Guid.NewGuid(), date, "Paiement facture " + _invoice.Invoice.Data.InvoiceNumber, amount, nature, goods, amount - goods,
            _payment.SelectedItem as string ?? "Autre", null, _invoice.Invoice.Data.ClientName ?? _invoice.Invoice.FolderClient,
            projects.Length == 1 ? projects[0] : null,
            _invoice.Invoice.Data.InvoiceNumber!, _invoice.Invoice.RelativePath, DateTimeOffset.UtcNow);
        Close(true);
    }

    private static bool ReadMoney(string? text, out decimal value)
    {
        if (!FilamentPrice.TryParse(text, out value)) return false;
        value = Math.Round(value, 2, MidpointRounding.AwayFromZero); return true;
    }
}

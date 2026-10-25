using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Purchases;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Entry of one purchase in the purchase register.</summary>
public sealed class PurchaseEntryWindow : DialogWindow
{
    private readonly TextBox _date = new() { PlaceholderText = "jj/mm/aaaa" };
    private readonly ComboBox _supplier = new() { IsEditable = true, PlaceholderText = "Nom du fournisseur" };
    private readonly TextBox _label = new() { PlaceholderText = "Ex. Bobines PLA, inserts laiton" };
    private readonly TextBox _amount = new() { PlaceholderText = "Montant payé" };
    private readonly ComboBox _payment = new() { ItemsSource = RevenueEntryWindow.PaymentMethods, SelectedIndex = 1 };
    private readonly TextBox _reference = new() { PlaceholderText = "Numéro de facture ou de commande" };
    private readonly TextBox _project = new() { PlaceholderText = "Référence du projet (facultatif)" };
    private readonly TextBlock _document = new() { Text = "Aucun justificatif joint.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _error = FormKit.ErrorText();

    public PurchaseEntry? Result { get; private set; }
    /// <summary>The supporting document chosen by the user; the caller copies it into the workspace after saving.</summary>
    public string? SourceDocumentPath { get; private set; }

    public PurchaseEntryWindow(IReadOnlyList<string> suppliers, DateOnly today)
    {
        Title = "Enregistrer un achat"; Width = 520; Height = 780; MinWidth = 440;
        _date.Text = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _supplier.ItemsSource = suppliers;
        var attach = new Button { Content = Icons.Label("IconUpload", "Joindre le justificatif (PDF)…", 16) };
        attach.Click += async (_, _) => await PickDocumentAsync();
        var save = new Button { Content = "Enregistrer" };
        save.Click += Save_Click;
        Layout("Enregistrer un achat", "Notez chaque achat à sa date, avec la facture du fournisseur comme justificatif.",
            [FormKit.Field("Date d’achat", _date), FormKit.Field("Fournisseur", _supplier), FormKit.Field("Libellé", _label), FormKit.Field("Montant payé", _amount, "€"),
             FormKit.Field("Mode de règlement", _payment), FormKit.Field("Pièce justificative", _reference), FormKit.Field("Projet", _project), attach, _document, _error], save);
        foreach (var field in new[] { _date, _label, _amount, _reference, _project }) field.TextChanged += (_, _) => FormKit.ClearError(_error);
    }

    private async Task PickDocumentAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir le justificatif", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Justificatif (PDF ou image)") { Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg"] }]
        });
        if (chosen.FirstOrDefault()?.TryGetLocalPath() is { } path) { SourceDocumentPath = path; _document.Text = "Justificatif : " + Path.GetFileName(path); }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!DateOnly.TryParseExact(_date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { FormKit.ShowError(_error, _date, "Saisissez la date au format jj/mm/aaaa."); return; }
        if (date > DateOnly.FromDateTime(DateTime.Today)) { FormKit.ShowError(_error, _date, "La date d’achat ne peut pas être dans le futur."); return; }
        var supplier = _supplier.Text?.Trim();
        if (string.IsNullOrWhiteSpace(supplier)) { FormKit.ShowError(_error, _supplier, "Renseignez le fournisseur."); return; }
        if (string.IsNullOrWhiteSpace(_label.Text)) { FormKit.ShowError(_error, _label, "Renseignez un libellé."); return; }
        if (!FilamentPrice.TryParse(_amount.Text, out var amount) || amount <= 0) { FormKit.ShowError(_error, _amount, "Saisissez un montant strictement positif."); return; }
        if (string.IsNullOrWhiteSpace(_reference.Text)) { FormKit.ShowError(_error, _reference, "Renseignez la référence de la facture (ou une référence interne)."); return; }
        Result = new PurchaseEntry(Guid.NewGuid(), date, supplier, _label.Text.Trim(), Math.Round(amount, 2, MidpointRounding.AwayFromZero), _payment.SelectedItem as string ?? "Autre",
            _reference.Text.Trim(), SourceDocumentPath, string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim(), DateTimeOffset.UtcNow);
        Close(true);
    }
}

/// <summary>Explicit confirmation with a mandatory reason; the purchase stays visible in the register.</summary>
public sealed class PurchaseCancelWindow : DialogWindow
{
    private readonly TextBox _reason = new() { PlaceholderText = "Ex. Montant erroné, doublon…" };
    private readonly TextBlock _error = FormKit.ErrorText();
    public string Reason => _reason.Text?.Trim() ?? string.Empty;

    public PurchaseCancelWindow(PurchaseEntry entry)
    {
        Title = "Annuler un achat"; Width = 520; Height = 470; MinWidth = 440;
        var confirm = new Button { Content = "Annuler l’achat" };
        var keep = new Button { Content = "Garder l’achat" };
        keep.Click += (_, _) => Close(false);
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_reason.Text)) { FormKit.ShowError(_error, _reason, "Indiquez le motif de l’annulation."); return; }
            Close(true);
        };
        _reason.TextChanged += (_, _) => FormKit.ClearError(_error);
        Layout($"Annuler « {entry.Label} » ?",
            $"{MS3DPRINT.Manager.Core.Revenues.RevenueExporter.Money(entry.Amount)} payés à {entry.Supplier} le {entry.PurchasedOn:dd/MM/yyyy} (pièce {entry.Reference}). La ligne reste visible, barrée, avec son motif, et n’est plus comptée dans les totaux. Pour corriger, saisissez ensuite un nouvel achat.",
            [FormKit.Field("Motif de l’annulation", _reason), _error], confirm, keep, destructive: true);
    }
}

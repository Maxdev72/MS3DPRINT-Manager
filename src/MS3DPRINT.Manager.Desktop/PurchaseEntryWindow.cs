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
    private readonly TextBox _correctionReason = new() { PlaceholderText = "Ex. Montant erroné, mauvaise date" };
    private readonly PurchaseEntry? _existing;

    public PurchaseEntry? Result { get; private set; }
    /// <summary>Why the line was corrected; only set when the window edits an existing line.</summary>
    public string CorrectionReason => _correctionReason.Text?.Trim() ?? string.Empty;
    /// <summary>The supporting document chosen by the user; the caller copies it into the workspace after saving.</summary>
    public string? SourceDocumentPath { get; private set; }

    public PurchaseEntryWindow(IReadOnlyList<string> suppliers, DateOnly today, PurchaseEntry? existing = null)
    {
        _existing = existing;
        Title = existing is null ? "Enregistrer un achat" : "Corriger un achat"; Width = 520; Height = existing is null ? 780 : 880; MinWidth = 440;
        _date.Text = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _supplier.ItemsSource = suppliers;
        var attach = new Button { Content = Icons.Label("IconUpload", existing is null ? "Joindre le justificatif (PDF)…" : "Remplacer le justificatif…", 16) };
        attach.Click += async (_, _) => await PickDocumentAsync();
        var save = new Button { Content = existing is null ? "Enregistrer" : "Enregistrer la correction" };
        save.Click += Save_Click;
        var body = new List<Control>
        {
            FormKit.Field("Date d’achat", _date), FormKit.Field("Fournisseur", _supplier), FormKit.Field("Libellé", _label), FormKit.Field("Montant payé", _amount, "€"),
            FormKit.Field("Mode de règlement", _payment), FormKit.Field("Pièce justificative", _reference), FormKit.Field("Projet", _project), attach, _document, _error
        };
        if (existing is not null)
        {
            body.Insert(0, FormKit.Field("Motif de la correction", _correctionReason, hint: "Les valeurs d’origine et ce motif restent consultables dans l’historique de la ligne."));
            _correctionReason.TextChanged += (_, _) => FormKit.ClearError(_error);
            _date.Text = existing.PurchasedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); _supplier.Text = existing.Supplier; _label.Text = existing.Label;
            _amount.Text = existing.Amount.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
            var methods = RevenueEntryWindow.PaymentMethods.Contains(existing.PaymentMethod) ? RevenueEntryWindow.PaymentMethods : RevenueEntryWindow.PaymentMethods.Append(existing.PaymentMethod).ToArray();
            _payment.ItemsSource = methods; _payment.SelectedItem = existing.PaymentMethod;
            _reference.Text = existing.Reference; _project.Text = existing.ProjectRef;
            _document.Text = existing.DocumentPath is null ? "Aucun justificatif joint." : "Justificatif actuel : " + Path.GetFileName(existing.DocumentPath);
        }
        Layout(existing is null ? "Enregistrer un achat" : "Corriger un achat",
            existing is null ? "Notez chaque achat à sa date, avec la facture du fournisseur comme justificatif." : "La ligne garde son historique : rien n’est effacé, la correction et son motif sont ajoutés au registre.",
            body, save);
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
        if (_existing is not null && string.IsNullOrWhiteSpace(_correctionReason.Text))
        { FormKit.ShowError(_error, _correctionReason, "Indiquez le motif de la correction."); return; }
        if (!DateOnly.TryParseExact(_date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { FormKit.ShowError(_error, _date, "Saisissez la date au format jj/mm/aaaa."); return; }
        if (date > DateOnly.FromDateTime(DateTime.Today)) { FormKit.ShowError(_error, _date, "La date d’achat ne peut pas être dans le futur."); return; }
        if (_existing is not null && date.Year != _existing.PurchasedOn.Year)
        { FormKit.ShowError(_error, _date, $"La date doit rester en {_existing.PurchasedOn.Year}. Pour changer d’année, annulez l’achat puis saisissez-le à nouveau."); return; }
        var supplier = _supplier.Text?.Trim();
        if (string.IsNullOrWhiteSpace(supplier)) { FormKit.ShowError(_error, _supplier, "Renseignez le fournisseur."); return; }
        if (string.IsNullOrWhiteSpace(_label.Text)) { FormKit.ShowError(_error, _label, "Renseignez un libellé."); return; }
        if (!FilamentPrice.TryParse(_amount.Text, out var amount) || amount <= 0) { FormKit.ShowError(_error, _amount, "Saisissez un montant strictement positif."); return; }
        if (string.IsNullOrWhiteSpace(_reference.Text)) { FormKit.ShowError(_error, _reference, "Renseignez la référence de la facture (ou une référence interne)."); return; }
        Result = new PurchaseEntry(_existing?.Id ?? Guid.NewGuid(), date, supplier, _label.Text.Trim(), Math.Round(amount, 2, MidpointRounding.AwayFromZero), _payment.SelectedItem as string ?? "Autre",
            _reference.Text.Trim(), SourceDocumentPath ?? _existing?.DocumentPath, string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim(), _existing?.RecordedAt ?? DateTimeOffset.UtcNow);
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

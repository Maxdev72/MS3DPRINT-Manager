using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Values proposed when the entry starts from a project; the user still confirms every field.</summary>
public sealed record RevenueSuggestion(string? ClientName, string? ClientFolder, string? ProjectRef, string? Label, decimal? Amount);

/// <summary>Free-form entry of one collection in the revenue book.</summary>
public sealed class RevenueEntryWindow : DialogWindow
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static IReadOnlyList<string> PaymentMethods { get; } = ["Virement", "Carte bancaire", "Espèces", "Chèque", "PayPal", "Autre"];
    private static readonly (string Label, RevenueNature Nature)[] Natures =
        [("Prestation de services", RevenueNature.Services), ("Vente de marchandises", RevenueNature.Goods), ("Mixte (marchandises et prestations)", RevenueNature.Mixed)];

    private readonly TextBox _date = new() { PlaceholderText = "jj/mm/aaaa" };
    private readonly ComboBox _client = new() { IsEditable = true, PlaceholderText = "Nom du client (facultatif)" };
    private readonly TextBox _label = new() { PlaceholderText = "Ex. Impression de 12 supports" };
    private readonly TextBox _amount = new() { PlaceholderText = "Montant encaissé" };
    private readonly ComboBox _nature = new() { ItemsSource = Natures.Select(n => n.Label).ToArray(), SelectedIndex = 0 };
    private readonly TextBox _goods = new() { PlaceholderText = "Part marchandises" };
    private readonly ComboBox _payment = new() { ItemsSource = PaymentMethods, SelectedIndex = 0 };
    private readonly TextBox _invoice = new() { PlaceholderText = "Numéro de facture Indy" };
    private readonly TextBox _project = new() { PlaceholderText = "Référence du projet (facultatif)" };
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly TextBlock _invoiceNote = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" }, IsVisible = false };

    public RevenueEntry? Result { get; private set; }
    /// <summary>The PDF the form was pre-filled from; the caller copies it into the workspace once the entry is saved.</summary>
    public string? SourcePdfPath { get; private set; }

    public RevenueEntryWindow(IReadOnlyList<string> clientNames, DateOnly today, string? invoicePdf = null)
    {
        Title = "Enregistrer un encaissement"; Width = 520; Height = 860; MinWidth = 440;
        _date.Text = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _client.ItemsSource = clientNames;
        _goods.IsVisible = false;
        _nature.SelectionChanged += (_, _) => { _goods.IsVisible = SelectedNature == RevenueNature.Mixed; FormKit.ClearError(_error); };
        var save = new Button { Content = "Enregistrer" };
        save.Click += Save_Click;
        var import = new Button { Content = Icons.Label("IconUpload", "Importer une facture PDF…", 16) };
        import.Click += async (_, _) => await PickInvoiceAsync();
        Layout("Enregistrer un encaissement", "Notez le paiement à sa date de réception, pas à la date de la facture.",
            [import, _invoiceNote, FormKit.Field("Date d’encaissement", _date), FormKit.Field("Client", _client), FormKit.Field("Libellé", _label),
             FormKit.Field("Montant encaissé", _amount, "€"), FormKit.Field("Nature", _nature), FormKit.Field("Part marchandises", _goods, "€", "Le reste est compté en prestations."),
             FormKit.Field("Mode de règlement", _payment), FormKit.Field("Pièce justificative (facture)", _invoice), FormKit.Field("Projet", _project), _error], save);
        foreach (var field in new[] { _date, _label, _amount, _goods, _invoice, _project }) field.TextChanged += (_, _) => FormKit.ClearError(_error);
        if (invoicePdf is not null) Opened += (_, _) => LoadInvoice(invoicePdf);
    }

    public void Suggest(RevenueSuggestion suggestion)
    {
        if (suggestion.ClientName is { } client) _client.Text = client;
        if (suggestion.ProjectRef is { } project) _project.Text = project;
        if (suggestion.Label is { } label && string.IsNullOrWhiteSpace(_label.Text)) _label.Text = label;
        if (suggestion.Amount is > 0 and { } amount) _amount.Text = amount.ToString("0.00", French);
    }

    private async Task PickInvoiceAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir la facture au format PDF", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Facture PDF") { Patterns = ["*.pdf"] }]
        });
        if (chosen.FirstOrDefault()?.TryGetLocalPath() is { } path) LoadInvoice(path);
    }

    /// <summary>Reads the PDF locally and proposes values; the user still checks every field and gives the collection date.</summary>
    public void LoadInvoice(string pdfPath)
    {
        var data = IndyInvoiceReader.Read(pdfPath);
        _invoiceNote.IsVisible = true;
        SourcePdfPath = pdfPath;
        if (data is null) { _invoiceNote.Text = "Cette facture n’a pas pu être lue automatiquement. Saisissez les informations à la main ; le PDF reste joint."; return; }
        if (data.InvoiceNumber is { } number) { _invoice.Text = number; if (string.IsNullOrWhiteSpace(_label.Text)) _label.Text = "Facture " + number; }
        if (data.TotalTtc is { } total) _amount.Text = total.ToString("0.00", French);
        if (data.ClientName is { } client) _client.Text = client;
        if (data.Nature is { } nature) _nature.SelectedIndex = Array.FindIndex(Natures, n => n.Nature == nature);
        _invoiceNote.Text = "Facture lue : vérifiez les champs et indiquez la date à laquelle le paiement a été reçu"
            + (data.IssuedOn is { } issued ? $" (facture émise le {issued:dd/MM/yyyy})." : ".")
            + (data.Nature == RevenueNature.Mixed ? " La facture mélange vente et prestation : renseignez la part marchandises." : "");
    }

    private RevenueNature SelectedNature => Natures[Math.Max(0, _nature.SelectedIndex)].Nature;

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!DateOnly.TryParseExact(_date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { FormKit.ShowError(_error, _date, "Saisissez la date au format jj/mm/aaaa."); return; }
        if (date > DateOnly.FromDateTime(DateTime.Today))
        { FormKit.ShowError(_error, _date, "La date d’encaissement ne peut pas être dans le futur."); return; }
        if (string.IsNullOrWhiteSpace(_label.Text)) { FormKit.ShowError(_error, _label, "Renseignez un libellé."); return; }
        if (!FilamentPrice.TryParse(_amount.Text, out var amount) || amount <= 0)
        { FormKit.ShowError(_error, _amount, "Saisissez un montant strictement positif."); return; }
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        decimal goods = 0, services = amount;
        var nature = SelectedNature;
        if (nature == RevenueNature.Goods) { goods = amount; services = 0; }
        else if (nature == RevenueNature.Mixed)
        {
            if (!FilamentPrice.TryParse(_goods.Text, out goods) || goods <= 0 || Math.Round(goods, 2, MidpointRounding.AwayFromZero) >= amount)
            { FormKit.ShowError(_error, _goods, "La part marchandises doit être positive et inférieure au montant total."); return; }
            goods = Math.Round(goods, 2, MidpointRounding.AwayFromZero);
            services = amount - goods;
        }
        if (string.IsNullOrWhiteSpace(_invoice.Text)) { FormKit.ShowError(_error, _invoice, "Renseignez le numéro de facture (ou une référence interne)."); return; }
        var client = _client.Text?.Trim();
        Result = new RevenueEntry(Guid.NewGuid(), date, _label.Text.Trim(), amount, nature, goods, services, _payment.SelectedItem as string ?? "Autre", null,
            string.IsNullOrWhiteSpace(client) ? null : client, string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim(), _invoice.Text.Trim(), SourcePdfPath, DateTimeOffset.UtcNow);
        Close(true);
    }
}

/// <summary>Explicit confirmation with a mandatory reason; the entry stays visible in the book.</summary>
public sealed class RevenueCancelWindow : DialogWindow
{
    private readonly TextBox _reason = new() { PlaceholderText = "Ex. Montant erroné, doublon…" };
    private readonly TextBlock _error = FormKit.ErrorText();
    public string Reason => _reason.Text?.Trim() ?? string.Empty;

    public RevenueCancelWindow(RevenueEntry entry)
    {
        Title = "Annuler une recette"; Width = 520; Height = 470; MinWidth = 440;
        var confirm = new Button { Content = "Annuler la recette" };
        var cancel = new Button { Content = "Garder la recette" };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_reason.Text)) { FormKit.ShowError(_error, _reason, "Indiquez le motif de l’annulation."); return; }
            Close(true);
        };
        _reason.TextChanged += (_, _) => FormKit.ClearError(_error);
        Layout($"Annuler « {entry.Label} » ?",
            $"{RevenueExporter.Money(entry.Amount)} encaissés le {entry.ReceivedOn:dd/MM/yyyy} (facture {entry.InvoiceNumber}). La ligne reste visible, barrée, avec son motif, et n’est plus comptée dans les totaux. Pour corriger, saisissez ensuite une nouvelle recette.",
            [FormKit.Field("Motif de l’annulation", _reason), _error], confirm, cancel, destructive: true);
    }
}

/// <summary>Workshop identity printed at the top of the exported book.</summary>
public sealed class RevenueHeaderWindow : DialogWindow
{
    private readonly TextBox _business = new() { PlaceholderText = "Nom de l’entreprise" };
    private readonly TextBox _owner = new() { PlaceholderText = "Nom de l’entrepreneur" };
    private readonly TextBox _siret = new() { PlaceholderText = "14 chiffres" };
    private readonly TextBox _address = new() { PlaceholderText = "Adresse postale" };
    private readonly TextBox _email = new() { PlaceholderText = "adresse@exemple.fr" };
    private readonly TextBox _phone = new() { PlaceholderText = "Téléphone" };
    public RevenueHeader Result => new(_business.Text?.Trim() ?? "", _siret.Text?.Trim() ?? "", _owner.Text?.Trim() ?? "",
        _address.Text?.Trim() ?? "", _email.Text?.Trim() ?? "", _phone.Text?.Trim() ?? "");

    public RevenueHeaderWindow(RevenueHeader current)
    {
        Title = "Informations de mon entreprise"; Width = 500; Height = 700; MinWidth = 420;
        _business.Text = current.BusinessName; _owner.Text = current.OwnerName; _siret.Text = current.Siret;
        _address.Text = current.Address; _email.Text = current.Email; _phone.Text = current.Phone;
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) => Close(true);
        Layout("Mon entreprise", "Ces informations figurent en haut du livre des recettes, du registre des achats et de leurs exports.",
            [FormKit.Field("Entreprise", _business), FormKit.Field("Entrepreneur", _owner), FormKit.Field("SIRET", _siret),
             FormKit.Field("Adresse", _address), FormKit.Field("E-mail", _email), FormKit.Field("Téléphone", _phone)], save);
    }
}

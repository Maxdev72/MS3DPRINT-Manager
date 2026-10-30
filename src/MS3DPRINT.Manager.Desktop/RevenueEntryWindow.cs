using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop.Clients;

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
    private readonly TextBox _correctionReason = new() { PlaceholderText = "Ex. Montant erroné, mauvaise date" };
    private readonly RevenueEntry? _existing;

    public RevenueEntry? Result { get; private set; }
    /// <summary>Why the line was corrected; only set when the window edits an existing line.</summary>
    public string CorrectionReason => _correctionReason.Text?.Trim() ?? string.Empty;
    /// <summary>The PDF the form was pre-filled from; the caller copies it into the workspace once the entry is saved.</summary>
    public string? SourcePdfPath { get; private set; }

    public RevenueEntryWindow(IReadOnlyList<string> clientNames, DateOnly today, string? invoicePdf = null, RevenueEntry? existing = null)
    {
        _existing = existing;
        Title = existing is null ? "Enregistrer un encaissement" : "Corriger un encaissement"; Width = 520; Height = existing is null ? 860 : 960; MinWidth = 440;
        _date.Text = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _client.ItemsSource = clientNames;
        _goods.IsVisible = false;
        _nature.SelectionChanged += (_, _) => { _goods.IsVisible = SelectedNature == RevenueNature.Mixed; FormKit.ClearError(_error); };
        var save = new Button { Content = existing is null ? "Enregistrer" : "Enregistrer la correction" };
        save.Click += Save_Click;
        var import = new Button { Content = Icons.Label("IconUpload", existing is null ? "Lire une facture (PDF, image)…" : "Remplacer la facture…", 16) };
        import.Click += async (_, _) => await PickInvoiceAsync();
        var paste = new Button { Content = "Coller une capture" };
        ToolTip.SetTip(paste, "Colle l’image copiée (capture d’écran, photo copiée) ou le fichier copié, puis la lit · Ctrl+V");
        paste.Click += async (_, _) => await PasteInvoiceAsync();
        var readers = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children = { import, paste } };
        var body = new List<Control>
        {
            readers, _invoiceNote, FormKit.Field("Date d’encaissement", _date), FormKit.Field("Client", _client), FormKit.Field("Libellé", _label),
            FormKit.Field("Montant encaissé", _amount, "€"), FormKit.Field("Nature", _nature), FormKit.Field("Part marchandises", _goods, "€", "Le reste est compté en prestations."),
            FormKit.Field("Mode de règlement", _payment), FormKit.Field("Pièce justificative (facture)", _invoice), FormKit.Field("Projet", _project), _error
        };
        if (existing is not null)
        {
            body.Insert(0, FormKit.Field("Motif de la correction", _correctionReason, hint: "Les valeurs d’origine et ce motif restent consultables dans l’historique de la ligne."));
            _correctionReason.TextChanged += (_, _) => FormKit.ClearError(_error);
            Fill(existing);
        }
        Layout(existing is null ? "Enregistrer un encaissement" : "Corriger un encaissement",
            existing is null ? "Notez le paiement à sa date de réception, pas à la date de la facture." : "La ligne garde son historique : rien n’est effacé, la correction et son motif sont ajoutés au livre.",
            body, save);
        foreach (var field in new[] { _date, _label, _amount, _goods, _invoice, _project }) field.TextChanged += (_, _) => FormKit.ClearError(_error);
        if (invoicePdf is not null) Opened += (_, _) => LoadInvoice(invoicePdf);
        DocumentReadingUi.EnableDrop(this, LoadInvoice);
        KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.V && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && FocusManager?.GetFocusedElement() is not TextBox) { e.Handled = true; await PasteInvoiceAsync(); } };
    }

    private void Fill(RevenueEntry entry)
    {
        _date.Text = entry.ReceivedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _client.Text = entry.ClientName; _label.Text = entry.Label; _amount.Text = entry.Amount.ToString("0.00", French);
        _nature.SelectedIndex = Math.Max(0, Array.FindIndex(Natures, n => n.Nature == entry.Nature));
        _goods.IsVisible = entry.Nature == RevenueNature.Mixed;
        if (entry.Nature == RevenueNature.Mixed) _goods.Text = entry.GoodsAmount.ToString("0.00", French);
        var methods = PaymentMethods.Contains(entry.PaymentMethod) ? PaymentMethods : PaymentMethods.Append(entry.PaymentMethod).ToArray();
        _payment.ItemsSource = methods; _payment.SelectedItem = entry.PaymentMethod;
        _invoice.Text = entry.InvoiceNumber; _project.Text = entry.ProjectRef;
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
        if (await DocumentReadingUi.PickAsync(this, "Choisir la facture (PDF ou image)") is { } path) await LoadInvoiceAsync(path);
    }

    private async Task PasteInvoiceAsync()
    {
        if (await DocumentReadingUi.PasteAsync(this) is { } path) await LoadInvoiceAsync(path);
        else { _invoiceNote.IsVisible = true; _invoiceNote.Text = "Rien à coller : copiez d’abord une capture d’écran, une image ou un fichier."; }
    }

    /// <summary>Reads the document locally and proposes values; the user still checks every field and gives the collection date.</summary>
    public void LoadInvoice(string path) => _ = LoadInvoiceAsync(path);

    public async Task LoadInvoiceAsync(string path)
    {
        _invoiceNote.IsVisible = true;
        SourcePdfPath = path;
        _invoiceNote.Text = "Lecture du document en cours…";
        DocumentReading reading;
        try { reading = await DocumentReader.ReadAsync(path, DocumentReadingUi.Context); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { _invoiceNote.Text = "Ce document n’a pas pu être lu : " + exception.Message + " Saisissez les informations à la main ; le fichier reste joint."; return; }
        if (reading.IsEmpty) { _invoiceNote.Text = DocumentReadingUi.Describe(reading, DocumentDirection.Revenue); return; }
        if (reading.Number is { } number) { _invoice.Text = number.Value; if (string.IsNullOrWhiteSpace(_label.Text)) _label.Text = "Facture " + number.Value; }
        else if (string.IsNullOrWhiteSpace(_label.Text) && reading.Label is { } label) _label.Text = label.Value;
        if (reading.TotalTtc is { } total) _amount.Text = DocumentReadingUi.Money(total.Value);
        if (reading.Party is { } client && reading.Direction != DocumentDirection.Purchase) _client.Text = client.Value;
        if (reading.Nature is { } nature) _nature.SelectedIndex = Array.FindIndex(Natures, n => n.Nature == nature);
        _invoiceNote.Text = DocumentReadingUi.Describe(reading, DocumentDirection.Revenue)
            + "\nIndiquez la date à laquelle le paiement a été reçu" + (reading.Date is { } issued ? $" (document daté du {issued.Value:dd/MM/yyyy})." : ".")
            + (reading.Nature == RevenueNature.Mixed ? " La facture mélange vente et prestation : renseignez la part marchandises." : "");
    }

    private RevenueNature SelectedNature => Natures[Math.Max(0, _nature.SelectedIndex)].Nature;

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_existing is not null && string.IsNullOrWhiteSpace(_correctionReason.Text))
        { FormKit.ShowError(_error, _correctionReason, "Indiquez le motif de la correction."); return; }
        if (!DateOnly.TryParseExact(_date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { FormKit.ShowError(_error, _date, "Saisissez la date au format jj/mm/aaaa."); return; }
        if (date > DateOnly.FromDateTime(DateTime.Today))
        { FormKit.ShowError(_error, _date, "La date d’encaissement ne peut pas être dans le futur."); return; }
        if (_existing is not null && date.Year != _existing.ReceivedOn.Year)
        { FormKit.ShowError(_error, _date, $"La date doit rester en {_existing.ReceivedOn.Year}. Pour changer d’année, annulez la recette puis saisissez-la à nouveau."); return; }
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
        Result = new RevenueEntry(_existing?.Id ?? Guid.NewGuid(), date, _label.Text.Trim(), amount, nature, goods, services, _payment.SelectedItem as string ?? "Autre", _existing?.ClientId,
            string.IsNullOrWhiteSpace(client) ? null : client, string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim(), _invoice.Text.Trim(),
            SourcePdfPath ?? _existing?.InvoicePdfPath, _existing?.RecordedAt ?? DateTimeOffset.UtcNow);
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
    private static readonly HttpClient LookupHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly IClientLookup _lookup;
    private readonly TextBox _business = new() { PlaceholderText = "Rechercher par nom ou SIRET" };
    private readonly TextBox _owner = new() { PlaceholderText = "Nom de l’entrepreneur" };
    private readonly TextBox _siret = new() { PlaceholderText = "14 chiffres" };
    private readonly TextBox _address = new() { PlaceholderText = "Tapez pour rechercher l’adresse" };
    private readonly TextBox _email = new() { PlaceholderText = "adresse@exemple.fr" };
    private readonly TextBox _phone = new() { PlaceholderText = "Téléphone" };
    private readonly ListBox _companyResults = new() { IsVisible = false, MaxHeight = 240, Classes = { "lookup-suggestions" } };
    private readonly ListBox _addressResults = new() { IsVisible = false, MaxHeight = 200, Classes = { "lookup-suggestions" } };
    private readonly TextBlock _lookupStatus = new() { IsVisible = false, FontSize = 13, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } };
    private CancellationTokenSource? _companySearch;
    private CancellationTokenSource? _addressSearch;
    private bool _applyingSuggestion;
    private bool _closed;
    private string _knownBusiness;
    private string _knownAddress;
    public RevenueHeader Result => new(_business.Text?.Trim() ?? "", _siret.Text?.Trim() ?? "", _owner.Text?.Trim() ?? "",
        _address.Text?.Trim() ?? "", _email.Text?.Trim() ?? "", _phone.Text?.Trim() ?? "");

    public RevenueHeaderWindow(RevenueHeader current, IClientLookup? lookup = null)
    {
        _lookup = lookup ?? new PublicClientLookup(LookupHttp);
        _knownBusiness = current.BusinessName.Trim(); _knownAddress = current.Address.Trim();
        Title = "Informations de mon entreprise"; Width = 500; Height = 700; MinWidth = 420;
        _business.Text = current.BusinessName; _owner.Text = current.OwnerName; _siret.Text = current.Siret;
        _address.Text = current.Address; _email.Text = current.Email; _phone.Text = current.Phone;
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) => Close(true);
        Avalonia.Automation.AutomationProperties.SetName(_companyResults, "Entreprises trouvées");
        Avalonia.Automation.AutomationProperties.SetName(_addressResults, "Adresses trouvées");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_lookupStatus, Avalonia.Automation.AutomationLiveSetting.Polite);
        _companyResults.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CompanySuggestion>((item, _) =>
            new StackPanel { Spacing = 4, Children =
            {
                new TextBlock { Text = item?.Name ?? "", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = string.Join(" · ", new[] { item?.Siret, item?.Address }.Where(v => !string.IsNullOrWhiteSpace(v))), FontSize = 13, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            } });
        _addressResults.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<AddressSuggestion>((item, _) =>
            new TextBlock { Text = item?.Label ?? "", FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _companyResults.SelectionChanged += (_, _) =>
        {
            if (_closed || _companyResults.SelectedItem is not CompanySuggestion company) return;
            _companySearch?.Cancel(); _addressSearch?.Cancel();
            _applyingSuggestion = true;
            _knownBusiness = company.Name.Trim(); _business.Text = company.Name;
            _siret.Text = company.Siret ?? "";
            if (!string.IsNullOrWhiteSpace(company.Address)) { _knownAddress = company.Address.Trim(); _address.Text = company.Address; }
            _applyingSuggestion = false;
            _companyResults.IsVisible = _addressResults.IsVisible = _lookupStatus.IsVisible = false;
            _companyResults.SelectedItem = null;
        };
        _addressResults.SelectionChanged += (_, _) =>
        {
            if (_closed || _addressResults.SelectedItem is not AddressSuggestion address) return;
            _addressSearch?.Cancel(); _applyingSuggestion = true;
            _knownAddress = address.Label.Trim(); _address.Text = address.Label;
            _applyingSuggestion = false;
            _addressResults.IsVisible = _lookupStatus.IsVisible = false; _addressResults.SelectedItem = null;
        };
        _business.TextChanged += async (_, _) =>
        {
            if (_closed || _applyingSuggestion) return;
            _companySearch?.Cancel(); _companyResults.IsVisible = false;
            var query = _business.Text?.Trim() ?? "";
            if (query == _knownBusiness) return;
            using var cancellation = new CancellationTokenSource(); _companySearch = cancellation;
            try { await SearchAsync(query, _companyResults, _lookup.SearchCompaniesAsync, cancellation.Token); }
            finally { if (ReferenceEquals(_companySearch, cancellation)) _companySearch = null; }
        };
        _address.TextChanged += async (_, _) =>
        {
            if (_closed || _applyingSuggestion) return;
            _addressSearch?.Cancel(); _addressResults.IsVisible = false;
            var query = _address.Text?.Trim() ?? "";
            if (query == _knownAddress) return;
            using var cancellation = new CancellationTokenSource(); _addressSearch = cancellation;
            try { await SearchAsync(query, _addressResults, _lookup.SearchAddressesAsync, cancellation.Token); }
            finally { if (ReferenceEquals(_addressSearch, cancellation)) _addressSearch = null; }
        };
        Layout("Mon entreprise", "Ces informations figurent en haut du livre des recettes, du registre des achats et de leurs exports.",
            [FormKit.Field("Entreprise", _business, hint: "Saisissez au moins 3 caractères, puis sélectionnez votre entreprise."), _companyResults,
             FormKit.Field("Entrepreneur", _owner), FormKit.Field("SIRET", _siret),
             FormKit.Field("Adresse", _address), _addressResults, _lookupStatus, FormKit.Field("E-mail", _email), FormKit.Field("Téléphone", _phone)], save);
    }

    private async Task SearchAsync<T>(string query, ListBox results, Func<string, CancellationToken, Task<IReadOnlyList<T>>> lookup, CancellationToken token)
    {
        _lookupStatus.IsVisible = false;
        if (query.Length < 3) return;
        try
        {
            await Task.Delay(350, token);
            var suggestions = await lookup(query, token);
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            results.SelectedItem = null;
            results.ItemsSource = suggestions; results.IsVisible = suggestions.Count > 0;
            _lookupStatus.Text = "Aucune suggestion trouvée. La saisie manuelle reste possible.";
            _lookupStatus.IsVisible = suggestions.Count == 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (_closed || token.IsCancellationRequested) return;
            _lookupStatus.Text = "Suggestions indisponibles. Vérifiez la connexion ou poursuivez la saisie manuellement.";
            _lookupStatus.IsVisible = true;
        }
    }

    protected override void OnDialogClosed()
    {
        _closed = true; _companySearch?.Cancel(); _addressSearch?.Cancel();
        _companyResults.IsVisible = _addressResults.IsVisible = false;
    }
}

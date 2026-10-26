using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Companion.Pages;

/// <summary>Note a purchase or a collection. Everything is saved on the phone first, then sent to the exchange folder.</summary>
public sealed class EntryPage : UserControl
{
    private static readonly string[] PaymentMethods = ["Carte bancaire", "Espèces", "Virement", "Chèque", "PayPal", "Autre"];
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private const string Separator = " · ";

    private readonly AppServices _app = AppServices.Current;
    private readonly (Control View, Func<int> Selected, Action<int> Select) _mode;
    private readonly (Control View, Func<int> Selected, Action<int> Select) _nature;
    private readonly (Control View, Func<int> Selected, Action<int> Select) _dateChoice;
    private readonly (Control View, Func<int> Selected, Action<int> Select) _paymentChoice;
    private readonly DatePicker _date = Ui.Ui.Input(new DatePicker { SelectedDate = DateTimeOffset.Now, IsVisible = false });
    private readonly AutoCompleteBox _supplier = Ui.Ui.Input(new AutoCompleteBox { Watermark = "Nom du fournisseur", MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains });
    private readonly AutoCompleteBox _client = Ui.Ui.Input(new AutoCompleteBox { Watermark = "Nom du client (facultatif)", MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains });
    private readonly TextBox _label = Ui.Ui.Input(new TextBox { Watermark = "Ex. Visserie, bobines PLA" });
    private readonly TextBox _amount = new()
    {
        Watermark = "0,00", FontSize = 38, FontWeight = FontWeight.SemiBold, Foreground = Pal.Text, Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 64,
        Padding = new Thickness(0, 8), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBox _goods = Ui.Ui.Input(new TextBox { Watermark = "Part marchandises en €" });
    private readonly TextBox _reference = Ui.Ui.Input(new TextBox { Watermark = "N° de facture ou de ticket (facultatif)" });
    private readonly TextBox _invoice = Ui.Ui.Input(new TextBox { Watermark = "N° de facture (facultatif)" });
    private readonly AutoCompleteBox _project = Ui.Ui.Input(new AutoCompleteBox { Watermark = "Référence du projet (facultatif)", MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains });
    private readonly Dictionary<Control, TextBlock> _errors = [];
    private readonly StackPanel _supplierField, _clientField, _purchaseExtras, _revenueExtras, _goodsField, _dateField;
    private readonly TextBlock _amountLabel = new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Pal.Faint, LetterSpacing = 1.2 };
    private readonly TextBlock _dateSummary = new() { FontSize = 14, Foreground = Pal.Muted };
    private readonly Image _preview = new() { MaxHeight = 240, Stretch = Stretch.Uniform, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _attachmentState = Ui.Ui.Muted("Aucune pièce jointe.");
    private readonly Button _removeAttachment = Ui.Ui.Quiet("Retirer la pièce jointe", Glyph.Close);
    private readonly Button _send = Ui.Ui.Primary("Envoyer au PC", Glyph.Send);
    private readonly ContentControl _result = new();
    private readonly StackPanel _form = new() { Spacing = 14, Margin = new Thickness(16, 12, 16, 24) };
    private readonly ScrollViewer _scroll;
    private readonly Avalonia.Threading.DispatcherTimer _draftTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private byte[]? _attachment;
    private string _attachmentExt = ".jpg";
    private bool _busy;
    private bool _restoring;

    public EntryPage()
    {
        _mode = Ui.Ui.Segmented(["Achat", "Recette"], _ => { ApplyMode(); DraftChanged(); });
        _nature = Ui.Ui.Segmented(["Prestation", "Marchandises", "Mixte"], _ => { ApplyMode(); DraftChanged(); });
        _dateChoice = Look.Chips(["Aujourd’hui", "Hier", "Autre date"], i => { ApplyDateChoice(i); DraftChanged(); });
        _paymentChoice = Look.Chips(PaymentMethods, _ => DraftChanged());
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(_amount, Avalonia.Input.TextInput.TextInputContentType.Number);
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(_goods, Avalonia.Input.TextInput.TextInputContentType.Number);

        // hero: the amount is what the user types first, so it is large and alone
        var euro = new TextBlock { Text = "€", FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = Pal.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var amountRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _amount, euro } };
        Grid.SetColumn(euro, 1);
        var amountError = new TextBlock { FontSize = 13, Foreground = Pal.Danger, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _errors[_amount] = amountError;
        var hero = new Border
        {
            Background = Pal.Raised, BorderBrush = Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 14, 20, 12),
            Child = new StackPanel { Spacing = 2, Children = { _amountLabel, amountRow, amountError } }
        };

        _dateField = F("Date", new StackPanel { Spacing = 10, Children = { _dateChoice.View, _date, _dateSummary } }, errorFor: _date);
        _supplierField = F("Fournisseur", _supplier);
        _clientField = F("Client", _client);
        _goodsField = F("Part marchandises", _goods, "Le reste est compté en prestations.");
        _purchaseExtras = new StackPanel { Spacing = 14, Children = { F("Pièce justificative", _reference, "Sans numéro, le PC écrira « Ticket du … ».") } };
        _revenueExtras = new StackPanel { Spacing = 14, Children = { F("Nature de la recette", _nature.View), _goodsField, F("Facture", _invoice) } };

        _removeAttachment.Click += (_, _) => SetAttachment(null, ".jpg");
        _removeAttachment.HorizontalAlignment = HorizontalAlignment.Left; _removeAttachment.IsVisible = false;
        var camera = Ui.Ui.Secondary("Photographier", Glyph.Camera); camera.Click += async (_, _) => await TakePhotoAsync();
        var gallery = Ui.Ui.Secondary("Galerie", Glyph.Image); gallery.Click += async (_, _) => await PickAsync();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        Grid.SetColumn(gallery, 1); buttons.Children.Add(camera); buttons.Children.Add(gallery);

        _send.Click += async (_, _) => await SendAsync();
        _project.SelectionChanged += (_, _) => OnProjectChosen();
        foreach (var box in new Control[] { _supplier, _client, _label, _amount, _goods, _reference, _invoice, _project })
        {
            if (box is TextBox text) text.TextChanged += (_, _) => { Clear(box); DraftChanged(); };
            else if (box is AutoCompleteBox auto) auto.TextChanged += (_, _) => { Clear(box); DraftChanged(); };
        }
        _date.SelectedDateChanged += (_, _) => { Clear(_date); UpdateDateSummary(); DraftChanged(); };
        _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); SaveDraft(); };

        _form.Children.Add(_mode.View); _form.Children.Add(_result); _form.Children.Add(hero);
        _form.Children.Add(Look.Section("Détails", _dateField, _supplierField, _clientField, F("Libellé", _label)));
        _form.Children.Add(Look.Section("Règlement", F("Mode de règlement", _paymentChoice.View), _purchaseExtras, _revenueExtras));
        _form.Children.Add(Look.Section("Rattachement", F("Projet", _project, "Pour rattacher la ligne au bilan d’un projet.")));
        _form.Children.Add(Look.Section("Justificatif", buttons, _preview, _attachmentState, _removeAttachment,
            Ui.Ui.Text("Une facture PDF reçue par e-mail ou dans une autre application : touchez « Partager » puis « MS3DPRINT Compagnon ».", 12, Pal.Faint)));

        _scroll = new ScrollViewer { Content = _form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // the send button stays under the thumb, whatever the length of the form
        var bar = new Border { Background = Pal.Sidebar, BorderBrush = Pal.Subtle, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10), Child = _send };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { _scroll, bar } };
        Grid.SetRow(bar, 1);
        Content = root;
        _app.Changed += RefreshSuggestions;
        ApplyMode(); ApplyDateChoice(0); RefreshSuggestions();
    }

    /// <summary>A labelled field with its own error line, so the message appears where the problem is.</summary>
    private StackPanel F(string label, Control input, string? hint = null, Control? errorFor = null)
    {
        var field = Ui.Ui.Field(label, input, hint);
        var error = new TextBlock { FontSize = 13, Foreground = Pal.Danger, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        field.Children.Add(error);
        _errors[errorFor ?? input] = error;
        return field;
    }

    private bool IsRevenue => _mode.Selected() == 1;
    private string Payment => PaymentMethods[Math.Clamp(_paymentChoice.Selected(), 0, PaymentMethods.Length - 1)];

    private static void SetLabel(StackPanel field, string text) { if (field.Children.FirstOrDefault() is TextBlock label) label.Text = text; }

    private void ApplyMode()
    {
        var revenue = IsRevenue;
        _supplierField.IsVisible = !revenue; _clientField.IsVisible = revenue;
        _purchaseExtras.IsVisible = !revenue; _revenueExtras.IsVisible = revenue;
        _goodsField.IsVisible = revenue && _nature.Selected() == 2;
        SetLabel(_dateField, revenue ? "Date d’encaissement" : "Date d’achat");
        _amountLabel.Text = revenue ? "MONTANT ENCAISSÉ" : "MONTANT PAYÉ";
        if (_send.Content is StackPanel row && row.Children.OfType<TextBlock>().FirstOrDefault() is { } label) label.Text = revenue ? "Envoyer la recette au PC" : "Envoyer l’achat au PC";
        _result.Content = null;
    }

    /// <summary>Today and yesterday are one tap; any other day opens the date wheel.</summary>
    private void ApplyDateChoice(int choice)
    {
        _date.IsVisible = choice == 2;
        if (choice == 0) _date.SelectedDate = DateTimeOffset.Now;
        else if (choice == 1) _date.SelectedDate = DateTimeOffset.Now.AddDays(-1);
        UpdateDateSummary();
    }

    private void UpdateDateSummary() =>
        _dateSummary.Text = DateOnly.FromDateTime((_date.SelectedDate ?? DateTimeOffset.Now).Date).ToString("dddd d MMMM yyyy", French);

    private void RefreshSuggestions()
    {
        var snapshot = _app.Snapshot;
        _supplier.ItemsSource = Merge(_app.Data.Suppliers, snapshot?.Suppliers);
        _client.ItemsSource = Merge(_app.Data.Clients, snapshot?.Clients.Select(c => c.Name));
        _project.ItemsSource = snapshot?.Projects.Where(p => !p.Archived).Select(ProjectLabel).ToList() ?? new List<string>();
    }

    private static List<string> Merge(IEnumerable<string> first, IEnumerable<string>? second) =>
        first.Concat(second ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string ProjectLabel(SnapshotProject p) => p.Reference + Separator + p.Title;

    private static string? ProjectReferenceOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cut = text.IndexOf(Separator, StringComparison.Ordinal);
        return (cut > 0 ? text[..cut] : text).Trim();
    }

    private void OnProjectChosen()
    {
        if (_app.Snapshot is not { } snapshot || ProjectReferenceOf(_project.Text) is not { } reference) return;
        var project = snapshot.Projects.FirstOrDefault(p => string.Equals(p.Reference, reference, StringComparison.OrdinalIgnoreCase));
        if (project is null) return;
        if (IsRevenue && string.IsNullOrWhiteSpace(_client.Text)) _client.Text = project.ClientName;
        if (string.IsNullOrWhiteSpace(_label.Text)) _label.Text = project.Title;
    }

    /// <summary>Opens the form pre-filled from a project of the PC.</summary>
    public void Prefill(bool revenue, SnapshotProject project)
    {
        _mode.Select(revenue ? 1 : 0); ApplyMode();
        _project.Text = ProjectLabel(project);
        if (revenue) _client.Text = project.ClientName;
        _label.Text = project.Title;
        if (revenue && project.Quoted is { } quoted && quoted > project.Collected) _amount.Text = (quoted - project.Collected).ToString("0.00", French);
        _result.Content = null;
        _scroll.ScrollToHome();
    }

    private void Clear(Control control)
    {
        if (control is TemplatedControl t) t.BorderBrush = Pal.Border;
        if (_errors.TryGetValue(control, out var error)) error.IsVisible = false;
    }

    /// <summary>Shows the message under the faulty field and scrolls to it.</summary>
    private bool Invalid(Control control, string message)
    {
        if (control is TemplatedControl t && !ReferenceEquals(control, _amount)) t.BorderBrush = Pal.Danger;
        if (_errors.TryGetValue(control, out var error)) { error.Text = message; error.IsVisible = true; }
        else _result.Content = Ui.Ui.Banner(message, BannerKind.Error);
        control.BringIntoView();
        return false;
    }

    private void SetAttachment(byte[]? bytes, string extension, string? name = null)
    {
        _attachment = bytes; _attachmentExt = extension;
        var isImage = bytes is not null && extension == ".jpg";
        _preview.Source = isImage ? new Bitmap(new MemoryStream(bytes!)) : null;
        _preview.IsVisible = isImage; _removeAttachment.IsVisible = bytes is not null;
        _attachmentState.Text = bytes is null ? "Aucune pièce jointe."
            : isImage ? $"Photo prête ({bytes.Length / 1024} Ko)." : $"Facture PDF jointe{(name is null ? "" : " : " + name)} ({bytes.Length / 1024} Ko).";
    }

    private async Task TakePhotoAsync()
    {
        if (MainActivity.Instance is not { } activity) { _result.Content = Ui.Ui.Banner("L’appareil photo n’est pas disponible.", BannerKind.Error); return; }
        try
        {
            SaveDraft();
            var bytes = await CameraCapture.TakePhotoAsync(activity);
            if (bytes is null) return;
            SetAttachment(bytes, ".jpg"); _result.Content = null;
            _ = ReadDocumentAsync(bytes, "photo.jpg");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Java.Lang.Exception)
        { _result.Content = Ui.Ui.Banner("Photo impossible : " + exception.Message, BannerKind.Error); }
    }

    private async Task PickAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choisir une photo ou une facture PDF", AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.Pdf]
            });
            if (chosen.FirstOrDefault() is not { } file) return;
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Receive(memory.ToArray(), file.Name);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Java.Lang.Exception)
        { _result.Content = Ui.Ui.Banner("Fichier impossible : " + exception.Message, BannerKind.Error); }
    }

    public void ShowProblem(string message) { _result.Content = Ui.Ui.Banner(message, BannerKind.Error); _scroll.ScrollToHome(); }

    /// <summary>A photo, a screenshot or a PDF given by the gallery, the file picker or the Android "Share" menu.</summary>
    public void Receive(byte[] bytes, string name)
    {
        if (bytes.Length == 0) { _result.Content = Ui.Ui.Banner("Ce fichier est vide.", BannerKind.Error); return; }
        if (bytes.Length > 25 * 1024 * 1024) { _result.Content = Ui.Ui.Banner("Ce fichier est trop volumineux (25 Mo maximum).", BannerKind.Error); return; }
        if (DocumentReader.IsPdf(bytes)) SetAttachment(bytes, ".pdf", name);
        else
        {
            var normalized = PhotoTools.Normalize(bytes);
            if (normalized is null) { _result.Content = Ui.Ui.Banner("Ce fichier n’est ni une image ni un PDF lisible.", BannerKind.Error); return; }
            SetAttachment(normalized, ".jpg");
        }
        _result.Content = null;
        _scroll.ScrollToHome();
        // The original is read, not the reduced copy that travels to the PC: more pixels, better reading.
        _ = ReadDocumentAsync(bytes, name);
    }

    private AnalysisContext ReadingContext()
    {
        var snapshot = _app.Snapshot;
        return new AnalysisContext
        {
            KnownSuppliers = Merge(_app.Data.Suppliers, snapshot?.Suppliers).ToList(),
            KnownClients = Merge(_app.Data.Clients, snapshot?.Clients.Select(c => c.Name)).ToList(),
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    /// <summary>Reads the document on the phone (text of a PDF, or OCR of a photo or screenshot) and pre-fills the form. The user checks everything and gives the real dates.</summary>
    private async Task ReadDocumentAsync(byte[] bytes, string name)
    {
        var attached = _attachment;
        _result.Content = Ui.Ui.Banner("Lecture du document en cours…", BannerKind.Info);
        DocumentReading reading;
        try { reading = await DocumentReader.ReadAsync(bytes, name, ReadingContext()); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or Java.Lang.Exception)
        { _result.Content = Ui.Ui.Banner("Lecture automatique impossible (" + exception.Message + "). Saisissez les informations à la main ; la pièce reste jointe.", BannerKind.Info); return; }
        if (!ReferenceEquals(attached, _attachment)) return; // the attachment was removed or replaced while reading
        Apply(reading);
    }

    private readonly Dictionary<Control, string> _auto = [];

    /// <summary>Fills a field from the reading unless the user typed in it. A value put by an earlier reading is replaced, so choosing another document starts afresh.</summary>
    private void FillIfBlank(TextBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (string.IsNullOrWhiteSpace(box.Text) || _auto.TryGetValue(box, out var previous) && previous == box.Text) { box.Text = value; _auto[box] = value; }
    }

    private void FillIfBlank(AutoCompleteBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (string.IsNullOrWhiteSpace(box.Text) || _auto.TryGetValue(box, out var previous) && previous == box.Text) { box.Text = value; _auto[box] = value; }
    }

    private void Apply(DocumentReading reading)
    {
        if (reading.IsEmpty) { _result.Content = Ui.Ui.Banner(ReadingSummary.Describe(reading, IsRevenue ? DocumentDirection.Revenue : DocumentDirection.Purchase), BannerKind.Info); return; }
        var detected = reading.Direction;
        if (detected == DocumentDirection.Revenue && !IsRevenue) _mode.Select(1);
        else if (detected == DocumentDirection.Purchase && IsRevenue) _mode.Select(0);
        ApplyMode();
        var revenue = IsRevenue;
        if (reading.TotalTtc is { } total) FillIfBlank(_amount, total.Value.ToString("0.00", French));
        if (reading.Party is { } party)
        {
            var known = (revenue ? _app.Snapshot?.Clients.Select(c => c.Name) : _app.Snapshot?.Suppliers)?.FirstOrDefault(n => string.Equals(n, party.Value, StringComparison.CurrentCultureIgnoreCase));
            if (revenue) FillIfBlank(_client, known ?? party.Value); else FillIfBlank(_supplier, known ?? party.Value);
        }
        if (reading.Number is { } number) FillIfBlank(revenue ? _invoice : _reference, number.Value);
        else if (!revenue && reading.Date is { } day) FillIfBlank(_reference, $"Ticket du {day.Value:dd/MM/yyyy}");
        if (reading.Label is { } label) FillIfBlank(_label, revenue && reading.Number is { } n ? "Facture " + n.Value : label.Value);
        if (!revenue && reading.Payment is { IsReliable: true } payment && Array.IndexOf(PaymentMethods, payment.Value) is >= 0 and var index) _paymentChoice.Select(index);
        if (revenue && reading.Nature is { } nature) _nature.Select(nature switch { RevenueNature.Goods => 1, RevenueNature.Mixed => 2, _ => 0 });
        if (!revenue && reading.Date is { } date && date.Value <= DateOnly.FromDateTime(DateTime.Today))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var choice = date.Value == today ? 0 : date.Value == today.AddDays(-1) ? 1 : 2;
            _dateChoice.Select(choice);
            if (choice == 2) _date.SelectedDate = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue));
            ApplyDateChoice(choice);
        }
        ApplyMode();
        var text = ReadingSummary.Describe(reading, revenue ? DocumentDirection.Revenue : DocumentDirection.Purchase);
        if (revenue) text += "\nIndiquez la date à laquelle le paiement a été reçu" + (reading.Date is { } issued ? $" (document daté du {issued.Value:dd/MM/yyyy})." : ".");
        var uncertain = reading.TotalTtc is not { IsReliable: true } || reading.Date is { IsReliable: false };
        _result.Content = Ui.Ui.Banner(text, uncertain ? BannerKind.Warning : BannerKind.Info);
        DraftChanged();
    }

    // ---- draft: what was typed survives a closed app --------------------------------------------------------------------------------------------

    private void DraftChanged() { if (!_restoring) { _draftTimer.Stop(); _draftTimer.Start(); } }

    /// <summary>Writes the form to the phone's memory.</summary>
    public void SaveDraft()
    {
        var draft = new EntryDraft
        {
            Mode = _mode.Selected(), Nature = _nature.Selected(), Supplier = _supplier.Text ?? "", Client = _client.Text ?? "", Label = _label.Text ?? "", Amount = _amount.Text ?? "",
            Goods = _goods.Text ?? "", Payment = Payment, Reference = _reference.Text ?? "", Invoice = _invoice.Text ?? "", Project = _project.Text ?? "",
            Date = _date.SelectedDate?.DateTime
        };
        _app.Data.Draft = draft.IsEmpty ? null : draft;
        _app.Store.Save(_app.Data);
    }

    /// <summary>Puts back what was typed before the app was closed.</summary>
    public void RestoreDraft()
    {
        if (_app.Data.Draft is not { IsEmpty: false } draft) return;
        _restoring = true;
        try
        {
            _mode.Select(Math.Clamp(draft.Mode, 0, 1)); _nature.Select(Math.Clamp(draft.Nature, 0, 2));
            _supplier.Text = draft.Supplier; _client.Text = draft.Client; _label.Text = draft.Label; _amount.Text = draft.Amount; _goods.Text = draft.Goods;
            _reference.Text = draft.Reference; _invoice.Text = draft.Invoice; _project.Text = draft.Project;
            var payment = Array.IndexOf(PaymentMethods, draft.Payment);
            if (payment >= 0) _paymentChoice.Select(payment);
            if (draft.Date is { } date && DateOnly.FromDateTime(date) <= DateOnly.FromDateTime(DateTime.Today))
            {
                var day = DateOnly.FromDateTime(date); var today = DateOnly.FromDateTime(DateTime.Today);
                _date.SelectedDate = new DateTimeOffset(date);
                var choice = day == today ? 0 : day == today.AddDays(-1) ? 1 : 2;
                _dateChoice.Select(choice); _date.IsVisible = choice == 2; UpdateDateSummary();
            }
            ApplyMode();
            _result.Content = Ui.Ui.Banner("Votre saisie en cours a été retrouvée.", BannerKind.Info);
        }
        finally { _restoring = false; }
    }

    /// <summary>A photo taken while Android had closed the app.</summary>
    public void AttachRecovered(byte[] photo) { SetAttachment(photo, ".jpg"); _result.Content = Ui.Ui.Banner("Votre photo a été retrouvée.", BannerKind.Info); _scroll.ScrollToHome(); _ = ReadDocumentAsync(photo, "photo.jpg"); }

    // ---- sending -------------------------------------------------------------------------------------------------------------------------------

    private async Task SendAsync()
    {
        if (_busy) return;
        var date = DateOnly.FromDateTime((_date.SelectedDate ?? DateTimeOffset.Now).Date);
        if (date > DateOnly.FromDateTime(DateTime.Today)) { Invalid(_date, "La date ne peut pas être dans le futur."); return; }
        var revenue = IsRevenue;
        var who = (revenue ? _client.Text : _supplier.Text)?.Trim() ?? "";
        if (!revenue && who.Length == 0) { Invalid(_supplier, "Renseignez le fournisseur."); return; }
        if (string.IsNullOrWhiteSpace(_label.Text)) { Invalid(_label, "Renseignez le libellé."); return; }
        if (!FilamentPrice.TryParse(_amount.Text, out var amount) || amount <= 0) { Invalid(_amount, "Saisissez un montant strictement positif (par exemple 18,90)."); return; }
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var payment = Payment;
        var project = ProjectReferenceOf(_project.Text);
        var label = _label.Text.Trim();
        var id = Guid.NewGuid();
        string baseName; byte[] json; string? attachmentName = null;
        var item = new HistoryItem { Id = id, Kind = revenue ? "recette" : "achat", Title = who.Length > 0 ? who : "Sans nom", Label = label, Amount = amount, Date = date, ProjectRef = project, AttachmentExt = _attachment is null ? null : _attachmentExt };

        if (revenue)
        {
            var nature = _nature.Selected() switch { 1 => RevenueNature.Goods, 2 => RevenueNature.Mixed, _ => RevenueNature.Services };
            decimal goods = nature == RevenueNature.Goods ? amount : 0m;
            if (nature == RevenueNature.Mixed)
            {
                if (!FilamentPrice.TryParse(_goods.Text, out goods) || goods <= 0 || Math.Round(goods, 2, MidpointRounding.AwayFromZero) >= amount)
                { Invalid(_goods, "La part marchandises doit être positive et inférieure au montant total."); return; }
                goods = Math.Round(goods, 2, MidpointRounding.AwayFromZero);
            }
            var draft = new MobileRevenue(id, date, label, amount, nature, goods, payment, who.Length == 0 ? null : who, string.IsNullOrWhiteSpace(_invoice.Text) ? null : _invoice.Text.Trim(), project, null, DateTimeOffset.Now);
            baseName = MobileRevenueExchange.BaseName(draft);
            if (_attachment is not null) attachmentName = baseName + _attachmentExt;
            json = MobileRevenueExchange.Serialize(draft with { PhotoFileName = attachmentName });
            item.Title = who.Length > 0 ? who : "Client non précisé";
        }
        else
        {
            var draft = new MobilePurchase(id, date, who, label, amount, payment, string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim(), project, null, DateTimeOffset.Now);
            baseName = MobileExchange.BaseName(draft);
            if (_attachment is not null) attachmentName = baseName + _attachmentExt;
            json = MobileExchange.Serialize(draft with { PhotoFileName = attachmentName });
        }
        item.BaseName = baseName;

        _busy = true; _send.IsEnabled = false;
        try
        {
            var saved = await _app.EnqueueAsync(item, json, _attachment);
            _app.Data.DefaultPayment = payment;
            LocalStore.Remember(revenue ? _app.Data.Clients : _app.Data.Suppliers, who);
            _app.Store.Save(_app.Data);
            _result.Content = saved.Status == "Sent"
                ? Ui.Ui.Banner($"Envoyé : {item.Title}, {amount.ToString("0.00", French)} €. Il apparaîtra sur le PC après la synchronisation Nextcloud.", BannerKind.Success)
                : Ui.Ui.Banner($"Enregistré sur le téléphone : {item.Title}, {amount.ToString("0.00", French)} €. L’envoi est en attente" + (saved.Error is null ? "." : " : " + saved.Error) + " Voir l’onglet Envois.", BannerKind.Warning);
            ResetForm();
            _scroll.ScrollToHome();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { _result.Content = Ui.Ui.Banner("Enregistrement impossible : " + exception.Message, BannerKind.Error); _scroll.ScrollToHome(); }
        finally { _busy = false; _send.IsEnabled = true; }
    }

    private void ResetForm()
    {
        _restoring = true;
        try
        {
            _supplier.Text = ""; _client.Text = ""; _label.Text = ""; _amount.Text = ""; _goods.Text = ""; _reference.Text = ""; _invoice.Text = ""; _project.Text = "";
            _dateChoice.Select(0); ApplyDateChoice(0); SetAttachment(null, ".jpg");
            foreach (var error in _errors.Values) error.IsVisible = false;
        }
        finally { _restoring = false; }
        _draftTimer.Stop(); _app.Data.Draft = null; _app.Store.Save(_app.Data);
        RefreshSuggestions();
    }
}

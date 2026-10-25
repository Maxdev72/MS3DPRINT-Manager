using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Mobile;

namespace MS3DPRINT.Manager.Companion;

/// <summary>One screen: note a purchase, photograph the receipt, send it to the exchange folder. The computer imports it later.</summary>
public sealed class MainView : UserControl
{
    private static readonly string[] PaymentMethods = ["Carte bancaire", "Espèces", "Virement", "Chèque", "PayPal", "Autre"];
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly LocalState _state = LocalState.Load();
    private readonly DatePicker _date = new() { SelectedDate = DateTimeOffset.Now, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly AutoCompleteBox _supplier = new() { Watermark = "Nom du fournisseur", MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains };
    private readonly TextBox _label = new() { Watermark = "Ex. Visserie, bobines PLA" };
    private readonly TextBox _amount = new() { Watermark = "Montant payé en €" };
    private readonly ComboBox _payment = new() { ItemsSource = PaymentMethods, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _reference = new() { Watermark = "N° de facture ou de ticket (facultatif)" };
    private readonly TextBox _project = new() { Watermark = "Référence du projet (facultatif)" };
    private readonly Image _preview = new() { MaxHeight = 220, Stretch = Stretch.Uniform, IsVisible = false };
    private readonly TextBlock _photoState = new() { Text = "Aucune photo.", Opacity = 0.7 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _folderLabel = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly StackPanel _history = new() { Spacing = 6 };
    private readonly Button _send = new() { Content = "Envoyer au PC", MinHeight = 56, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 18 };
    private IStorageFolder? _folder;
    private byte[]? _photo;

    public MainView()
    {
        _supplier.ItemsSource = _state.Suppliers;
        _amount.Text = ""; Avalonia.Input.TextInput.TextInputOptions.SetContentType(_amount, Avalonia.Input.TextInput.TextInputContentType.Number);
        var photo = new Button { Content = "Photographier le ticket", MinHeight = 52, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        photo.Click += async (_, _) => await TakePhotoAsync();
        var removePhoto = new Button { Content = "Retirer la photo", HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        removePhoto.Click += (_, _) => SetPhoto(null);
        _removePhoto = removePhoto;
        var folder = new Button { Content = "Choisir le dossier d’échange", MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        folder.Click += async (_, _) => await PickFolderAsync();
        _send.Click += async (_, _) => await SendAsync();

        Control Field(string label, Control input) => new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, input } };
        foreach (var box in new Control[] { _supplier, _label, _amount, _reference, _project }) box.MinHeight = 48;
        _payment.MinHeight = 48; _date.MinHeight = 48;

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18, 24, 18, 32), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Nouvel achat", FontSize = 28, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Notez l’achat et photographiez le ticket : le PC l’importera dans le registre des achats, après votre confirmation.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                    Field("Date d’achat", _date), Field("Fournisseur", _supplier), Field("Libellé", _label), Field("Montant payé (€)", _amount), Field("Mode de règlement", _payment),
                    Field("Pièce justificative", _reference), Field("Projet", _project),
                    new StackPanel { Spacing = 8, Children = { photo, _preview, _photoState, removePhoto } },
                    _status, _send,
                    new Border { Height = 1, Background = Brushes.Gray, Opacity = 0.3, Margin = new Thickness(0, 8) },
                    new TextBlock { Text = "Dossier d’échange", FontSize = 18, FontWeight = FontWeight.SemiBold }, _folderLabel, folder,
                    new TextBlock { Text = "Derniers envois", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) }, _history
                }
            }
        };
        RenderHistory();
        Loaded += async (_, _) => await RestoreFolderAsync();
    }

    private readonly Button _removePhoto;

    private void Say(string message, bool error = false)
    {
        _status.Text = message;
        _status.Foreground = error ? Brushes.IndianRed : Brushes.MediumSeaGreen;
    }

    private void SetPhoto(byte[]? bytes)
    {
        _photo = bytes;
        _preview.Source = bytes is null ? null : new Bitmap(new MemoryStream(bytes));
        _preview.IsVisible = bytes is not null; _removePhoto.IsVisible = bytes is not null;
        _photoState.Text = bytes is null ? "Aucune photo." : $"Photo prête ({bytes.Length / 1024} Ko).";
    }

    private async Task TakePhotoAsync()
    {
        if (MainActivity.Instance is not { } activity) { Say("L’appareil photo n’est pas disponible.", error: true); return; }
        try
        {
            var bytes = await CameraCapture.TakePhotoAsync(activity);
            if (bytes is null) { Say("Aucune photo prise."); return; }
            SetPhoto(bytes); Say("");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Java.Lang.Exception)
        { Say("Photo impossible : " + exception.Message, error: true); }
    }

    private async Task RestoreFolderAsync()
    {
        _folderLabel.Text = "Aucun dossier choisi. Choisissez le dossier « Echanges mobile » de votre Nextcloud.";
        if (_state.FolderBookmark is not { } bookmark || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            _folder = await storage.OpenFolderBookmarkAsync(bookmark);
            _folderLabel.Text = _folder is null ? "Le dossier d’échange n’est plus accessible : choisissez-le à nouveau." : "Dossier : " + (_state.FolderName ?? _folder.Name);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Java.Lang.Exception)
        { _folder = null; _folderLabel.Text = "Le dossier d’échange n’est plus accessible : choisissez-le à nouveau."; }
    }

    private async Task PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier « Echanges mobile »", AllowMultiple = false });
            if (picked.FirstOrDefault() is not { } folder) return;
            _folder = folder;
            _state.FolderBookmark = await folder.SaveBookmarkAsync(); _state.FolderName = folder.Name; _state.Save();
            _folderLabel.Text = "Dossier : " + folder.Name;
            Say("Dossier d’échange enregistré.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Java.Lang.Exception)
        { Say("Dossier inaccessible : " + exception.Message, error: true); }
    }

    private async Task SendAsync()
    {
        if (_folder is null) { Say("Choisissez d’abord le dossier d’échange (en bas de l’écran).", error: true); return; }
        var supplier = _supplier.Text?.Trim() ?? "";
        if (supplier.Length == 0) { Say("Renseignez le fournisseur.", error: true); return; }
        if (string.IsNullOrWhiteSpace(_label.Text)) { Say("Renseignez le libellé.", error: true); return; }
        if (!FilamentPrice.TryParse(_amount.Text, out var amount) || amount <= 0) { Say("Saisissez un montant strictement positif.", error: true); return; }
        var date = DateOnly.FromDateTime((_date.SelectedDate ?? DateTimeOffset.Now).Date);
        if (date > DateOnly.FromDateTime(DateTime.Today)) { Say("La date ne peut pas être dans le futur.", error: true); return; }

        _send.IsEnabled = false;
        try
        {
            var id = Guid.NewGuid();
            var draft = new MobilePurchase(id, date, supplier, _label.Text.Trim(), Math.Round(amount, 2, MidpointRounding.AwayFromZero), _payment.SelectedItem as string ?? "Autre",
                string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim(), string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim(), null, DateTimeOffset.Now);
            var baseName = MobileExchange.BaseName(draft);
            var purchase = _photo is null ? draft : draft with { PhotoFileName = baseName + ".jpg" };
            var json = MobileExchange.Serialize(purchase);
            // The photo goes first: the computer then never finds data whose photo has not arrived.
            if (_photo is not null) await WriteAsync(baseName + ".jpg", _photo);
            await WriteAsync(baseName + ".json", json);
            _state.Remember(new SentPurchase(purchase.Supplier, purchase.Label, purchase.Amount, purchase.PurchasedOn, _photo is not null));
            _supplier.ItemsSource = _state.Suppliers.ToList();
            RenderHistory();
            _supplier.Text = ""; _label.Text = ""; _amount.Text = ""; _reference.Text = ""; _project.Text = ""; SetPhoto(null);
            Say($"Envoyé : {purchase.Supplier}, {purchase.Amount.ToString("0.00", French)} €. Il apparaîtra sur le PC après la synchronisation.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException or Java.Lang.Exception)
        { Say("Envoi impossible : " + exception.Message, error: true); }
        finally { _send.IsEnabled = true; }
    }

    private async Task WriteAsync(string name, byte[] bytes)
    {
        var file = await _folder!.CreateFileAsync(name) ?? throw new IOException("Le dossier d’échange refuse la création du fichier.");
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private void RenderHistory()
    {
        _history.Children.Clear();
        if (_state.Sent.Count == 0) { _history.Children.Add(new TextBlock { Text = "Rien d’envoyé pour l’instant.", Opacity = 0.7 }); return; }
        foreach (var sent in _state.Sent)
            _history.Children.Add(new TextBlock { Text = $"{sent.PurchasedOn:dd/MM/yyyy} · {sent.Supplier} · {sent.Amount.ToString("0.00", French)} €{(sent.HasPhoto ? " · photo" : "")}\n{sent.Label}", TextWrapping = TextWrapping.Wrap });
    }
}

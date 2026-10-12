using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Desktop.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateClientWindow : DialogWindow
{
    private static readonly HttpClient LookupHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const int SuggestionDelayMs = 350;

    private readonly IClientLookup _lookup;
    private readonly TextBox _name = new() { PlaceholderText = "Tapez pour rechercher l’entreprise" };
    private readonly TextBox _firstName = new() { PlaceholderText = "Prénom" };
    private readonly TextBox _lastName = new() { PlaceholderText = "Nom" };
    private readonly TextBox _siret = new() { PlaceholderText = "14 chiffres" };
    private readonly TextBox _email = new() { PlaceholderText = "contact@exemple.fr" };
    private readonly TextBox _mobile = new() { PlaceholderText = "06 12 34 56 78" };
    private readonly TextBox _address = new() { PlaceholderText = "Tapez pour rechercher l’adresse" };
    private readonly ListBox _companyResults = new() { IsVisible = false, MaxHeight = 150 };
    private readonly ListBox _addressResults = new() { IsVisible = false, MaxHeight = 150 };
    private readonly TextBlock _codePreview = new() { FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly IReadOnlyList<string> _usedCodes;
    private readonly TextBlock _status = new() { FontSize = 12, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly CheckBox _individual = new() { Content = "Client particulier" };
    private CancellationTokenSource? _companySearch;
    private CancellationTokenSource? _addressSearch;
    private readonly string _existingCode;
    private bool _applyingSuggestion;

    public string ClientName => _name.Text?.Trim() ?? string.Empty;
    public string FirstName => _firstName.Text?.Trim() ?? string.Empty;
    public string LastName => _lastName.Text?.Trim() ?? string.Empty;
    /// <summary>Code of the client being completed; empty for a new client (the code is then generated automatically).</summary>
    public string Code => _existingCode;
    public string? Siret => string.IsNullOrWhiteSpace(_siret.Text) || IsIndividual ? null : _siret.Text.Trim();
    public string? Email => string.IsNullOrWhiteSpace(_email.Text) ? null : _email.Text.Trim();
    public string? Mobile => string.IsNullOrWhiteSpace(_mobile.Text) ? null : _mobile.Text.Trim();
    public string? Address => string.IsNullOrWhiteSpace(_address.Text) ? null : _address.Text.Trim();
    public bool IsIndividual => _individual.IsChecked == true;

    public CreateClientWindow(ClientSummary? existingClient = null, IClientLookup? lookup = null, IReadOnlyList<string>? usedCodes = null)
    {
        _lookup = lookup ?? new PublicClientLookup(LookupHttp);
        _usedCodes = usedCodes ?? [];
        _existingCode = existingClient?.ClientCode ?? string.Empty;
        Title = existingClient is null ? "Créer un client" : "Compléter la fiche client";
        Width = 500; Height = 800; MinWidth = 420; MinHeight = 480;
        if (existingClient is not null) _name.Text = existingClient.Title();

        var create = new Button { Content = existingClient is null ? "Créer le client" : "Enregistrer la fiche" };
        create.Click += (_, _) => TryClose();

        _individual.IsCheckedChanged += (_, _) =>
        {
            _name.IsVisible = !IsIndividual;
            _siret.IsVisible = !IsIndividual;
            _firstName.IsVisible = IsIndividual;
            _lastName.IsVisible = IsIndividual;
            HideSuggestions();
            UpdateCodePreview();
        };
        _firstName.IsVisible = _lastName.IsVisible = false;
        _firstName.TextChanged += (_, _) => UpdateCodePreview();
        _lastName.TextChanged += (_, _) => UpdateCodePreview();

        _name.TextChanged += async (_, _) =>
        {
            UpdateCodePreview();
            if (!_applyingSuggestion && !IsIndividual) await SearchCompaniesAsync(ClientName);
        };
        _address.TextChanged += async (_, _) =>
        {
            if (!_applyingSuggestion) await SearchAddressesAsync(_address.Text?.Trim() ?? string.Empty);
        };

        _companyResults.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CompanySuggestion>((item, _) => new TextBlock
        {
            Text = item is null ? string.Empty : item.Label, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        _companyResults.SelectionChanged += (_, _) =>
        {
            if (_companyResults.SelectedItem is not CompanySuggestion suggestion) return;
            ApplyCompany(suggestion);
        };
        _addressResults.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<AddressSuggestion>((item, _) => new TextBlock
        {
            Text = item is null ? string.Empty : item.Label, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        _addressResults.SelectionChanged += (_, _) =>
        {
            if (_addressResults.SelectedItem is not AddressSuggestion suggestion) return;
            _applyingSuggestion = true;
            _address.Text = suggestion.Label;
            _applyingSuggestion = false;
            _addressResults.IsVisible = false;
            _addressResults.SelectedItem = null;
        };

        Layout(existingClient is null ? "Nouveau client" : "Compléter la fiche client",
            existingClient is null ? "Un code client est attribué automatiquement à la création." : "Le dossier et les fichiers existants sont conservés.",
            [_codePreview, _individual, FormKit.Field("Nom de l’entreprise", _name), _companyResults,
             FormKit.Field("Prénom", _firstName), FormKit.Field("Nom", _lastName),
             FormKit.Field("SIRET (optionnel)", _siret),
             FormKit.Field("E-mail", _email), FormKit.Field("Portable", _mobile),
             FormKit.Field("Adresse", _address), _addressResults, _status, _error], create);
        UpdateCodePreview();
        Closed += (_, _) => { _companySearch?.Cancel(); _addressSearch?.Cancel(); };
    }

    private void TryClose()
    {
        var identityOk = IsIndividual
            ? NameNormalizer.Normalize(FirstName).Length > 0 && NameNormalizer.Normalize(LastName).Length > 0
            : NameNormalizer.Normalize(ClientName).Length > 0;
        if (!identityOk)
        {
            _error.Text = IsIndividual
                ? "Renseignez le prénom et le nom du client."
                : "Renseignez le nom de l’entreprise.";
            _error.IsVisible = true;
            return;
        }
        Close(true);
    }

    private void HideSuggestions()
    {
        _companySearch?.Cancel();
        _addressSearch?.Cancel();
        _companyResults.IsVisible = false;
        _addressResults.IsVisible = false;
    }

    private void ApplyCompany(CompanySuggestion suggestion)
    {
        _companySearch?.Cancel();
        _addressSearch?.Cancel();
        _applyingSuggestion = true;
        _name.Text = suggestion.Name;
        _siret.Text = suggestion.Siret ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(suggestion.Address)) _address.Text = suggestion.Address;
        _applyingSuggestion = false;
        _companyResults.IsVisible = false;
        _addressResults.IsVisible = false;
        _companyResults.SelectedItem = null;
        _status.IsVisible = false;
    }

    private async Task SearchCompaniesAsync(string query)
    {
        _companySearch?.Cancel();
        _companyResults.IsVisible = false;
        if (query.Length < 3) return;
        var cancellation = _companySearch = new CancellationTokenSource();
        try
        {
            await Task.Delay(SuggestionDelayMs, cancellation.Token);
            var results = await _lookup.SearchCompaniesAsync(query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _companyResults.ItemsSource = results;
            _companyResults.IsVisible = results.Count > 0;
            _status.IsVisible = false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            ShowLookupUnavailable();
        }
    }

    private async Task SearchAddressesAsync(string query)
    {
        _addressSearch?.Cancel();
        _addressResults.IsVisible = false;
        if (query.Length < 3) return;
        var cancellation = _addressSearch = new CancellationTokenSource();
        try
        {
            await Task.Delay(SuggestionDelayMs, cancellation.Token);
            var results = await _lookup.SearchAddressesAsync(query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _addressResults.ItemsSource = results;
            _addressResults.IsVisible = results.Count > 0;
            _status.IsVisible = false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            ShowLookupUnavailable();
        }
    }

    private void ShowLookupUnavailable()
    {
        _status.Text = "Suggestions indisponibles (connexion ?). La saisie manuelle reste possible.";
        _status.IsVisible = true;
    }

    private void UpdateCodePreview()
    {
        if (_existingCode.Length > 0)
        {
            _codePreview.Text = "Code client : " + _existingCode;
            return;
        }
        var identity = IsIndividual ? $"{FirstName} {LastName}" : ClientName;
        var code = ClientCodeSuggester.SuggestUnique(identity, _usedCodes);
        _codePreview.Text = code.Length == 0 ? "Code client : attribué dès que le nom est saisi" : "Code client attribué : " + code;
    }
}

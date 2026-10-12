using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Desktop.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateClientWindow : Window
{
    private static readonly HttpClient LookupHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const int SuggestionDelayMs = 350;

    private readonly IClientLookup _lookup;
    private readonly TextBox _name = new() { PlaceholderText = "Nom de l’entreprise (suggestions automatiques)" };
    private readonly TextBox _firstName = new() { PlaceholderText = "Prénom" };
    private readonly TextBox _lastName = new() { PlaceholderText = "Nom" };
    private readonly TextBox _code = new() { PlaceholderText = "Code client" };
    private readonly TextBox _siret = new() { PlaceholderText = "SIRET (optionnel)" };
    private readonly TextBox _address = new() { PlaceholderText = "Adresse (suggestions automatiques)" };
    private readonly ListBox _companyResults = new() { IsVisible = false, MaxHeight = 150 };
    private readonly ListBox _addressResults = new() { IsVisible = false, MaxHeight = 150 };
    private readonly TextBlock _status = new() { FontSize = 12, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _individual = new() { Content = "Client particulier" };
    private CancellationTokenSource? _companySearch;
    private CancellationTokenSource? _addressSearch;
    private bool _codeEdited;
    private bool _suggestingCode;
    private bool _applyingSuggestion;

    public string ClientName => _name.Text?.Trim() ?? string.Empty;
    public string FirstName => _firstName.Text?.Trim() ?? string.Empty;
    public string LastName => _lastName.Text?.Trim() ?? string.Empty;
    public string Code => _code.Text?.Trim().ToUpperInvariant() ?? string.Empty;
    public string? Siret => string.IsNullOrWhiteSpace(_siret.Text) || IsIndividual ? null : _siret.Text.Trim();
    public string? Address => string.IsNullOrWhiteSpace(_address.Text) ? null : _address.Text.Trim();
    public bool IsIndividual => _individual.IsChecked == true;

    public CreateClientWindow(ClientSummary? existingClient = null, IClientLookup? lookup = null)
    {
        _lookup = lookup ?? new PublicClientLookup(LookupHttp);
        _codeEdited = existingClient is not null;
        Title = existingClient is null ? "Créer un client" : "Compléter la fiche client";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 480; Height = 720; MinHeight = 520;
        if (existingClient is not null) { _name.Text = existingClient.DisplayName; _code.Text = existingClient.ClientCode; }

        var create = new Button { Content = existingClient is null ? "Créer" : "Enregistrer la fiche", HorizontalAlignment = HorizontalAlignment.Right };
        create.Click += (_, _) => TryClose();

        _individual.IsCheckedChanged += (_, _) =>
        {
            _name.IsVisible = !IsIndividual;
            _siret.IsVisible = !IsIndividual;
            _firstName.IsVisible = IsIndividual;
            _lastName.IsVisible = IsIndividual;
            HideSuggestions();
            SuggestCode();
        };
        _firstName.IsVisible = _lastName.IsVisible = false;

        _name.TextChanged += async (_, _) =>
        {
            SuggestCode();
            if (!_applyingSuggestion && !IsIndividual) await SearchCompaniesAsync(ClientName);
        };
        _firstName.TextChanged += (_, _) => SuggestCode();
        _lastName.TextChanged += (_, _) => SuggestCode();
        _code.TextChanged += (_, _) => { if (!_suggestingCode) _codeEdited = true; };
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

        var title = new TextBlock { Text = existingClient is null ? "Nouveau client" : "Compléter la fiche client", FontSize = 20 };
        var hint = new TextBlock { Text = existingClient is null ? string.Empty : "Le dossier et les fichiers existants sont conservés.", Classes = { "muted" } };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children = { title, hint, _individual, _name, _companyResults, _firstName, _lastName, _code, _siret, _address, _addressResults, _status, _error, create }
            }
        };
        Closed += (_, _) => { _companySearch?.Cancel(); _addressSearch?.Cancel(); };
    }

    private void TryClose()
    {
        var identityOk = IsIndividual
            ? NameNormalizer.Normalize(FirstName).Length > 0 && NameNormalizer.Normalize(LastName).Length > 0
            : NameNormalizer.Normalize(ClientName).Length > 0;
        if (!identityOk || NameNormalizer.Normalize(Code).Length == 0)
        {
            _error.Text = IsIndividual
                ? "Renseignez le prénom, le nom et le code client."
                : "Renseignez le nom de l’entreprise et le code client.";
            _error.IsVisible = true;
            return;
        }
        Close(true);
    }

    private void SuggestCode()
    {
        if (_codeEdited) return;
        _suggestingCode = true;
        _code.Text = ClientCodeSuggester.Suggest(IsIndividual ? $"{FirstName} {LastName}" : ClientName);
        _suggestingCode = false;
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
        SuggestCode();
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
}

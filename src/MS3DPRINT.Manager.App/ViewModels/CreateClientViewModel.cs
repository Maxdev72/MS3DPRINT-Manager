using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.App.Clients;
using System.Net.Http;
using System.Text.Json;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CreateClientViewModel : ObservableObject, IDisposable
{
    private static readonly HttpClient LookupHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly IClientLookup _lookup;
    private CancellationTokenSource? _companyCancellation;
    private CancellationTokenSource? _addressCancellation;
    private bool _applyingSuggestion;
    private bool _disposed;
    private string _address = string.Empty;
    private string _siret = string.Empty;
    private string _companyQuery = string.Empty;
    private string _lookupStatus = string.Empty;
    private IReadOnlyList<CompanySuggestion> _companySuggestions = [];
    private IReadOnlyList<AddressSuggestion> _addressSuggestions = [];
    private readonly string? _existingFolderName;
    private string _clientName = string.Empty;
    private string _clientCode = string.Empty;
    private bool _codeEdited;
    private ClientKind _kind = ClientKind.Professional;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;

    public CreateClientViewModel() : this(new PublicClientLookup(LookupHttp)) { }
    public CreateClientViewModel(IClientLookup lookup) => _lookup = lookup;

    public CreateClientViewModel(string existingFolderName, string? existingClientCode) : this()
    {
        _existingFolderName = existingFolderName ?? throw new ArgumentNullException(nameof(existingFolderName));
        _clientCode = string.IsNullOrWhiteSpace(existingClientCode) ? existingFolderName : existingClientCode;
        _codeEdited = true;
    }

    public string ClientName
    {
        get => _clientName;
        set
        {
            if (!SetProperty(ref _clientName, value)) return;
            OnPropertyChanged(nameof(NormalizedName));
            if (!_codeEdited)
            {
                _clientCode = ClientCodeSuggester.Suggest(value);
                OnPropertyChanged(nameof(ClientCode));
            }
            if (!_applyingSuggestion) CompanyQuery = value;
        }
    }

    public ClientKind Kind
    {
        get => _kind;
        set
        {
            if (!SetProperty(ref _kind, value)) return;
            _companyCancellation?.Cancel();
            CompanySuggestions = [];
            RefreshIdentity();
        }
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (!SetProperty(ref _firstName, value)) return;
            RefreshIdentity();
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (!SetProperty(ref _lastName, value)) return;
            RefreshIdentity();
        }
    }

    public string ClientCode
    {
        get => _clientCode;
        set
        {
            if (SetProperty(ref _clientCode, value)) _codeEdited = true;
        }
    }

    public string Address
    {
        get => _address;
        set { if (SetProperty(ref _address, value) && !_applyingSuggestion) _ = RefreshSuggestionsAsync(false); }
    }
    public string Siret { get => _siret; set => SetProperty(ref _siret, value); }
    public string CompanyQuery
    {
        get => _companyQuery;
        set { if (SetProperty(ref _companyQuery, value)) _ = RefreshSuggestionsAsync(true); }
    }
    public string LookupStatus { get => _lookupStatus; private set => SetProperty(ref _lookupStatus, value); }
    public IReadOnlyList<CompanySuggestion> CompanySuggestions { get => _companySuggestions; private set => SetProperty(ref _companySuggestions, value); }
    public IReadOnlyList<AddressSuggestion> AddressSuggestions { get => _addressSuggestions; private set => SetProperty(ref _addressSuggestions, value); }

    private async Task RefreshSuggestionsAsync(bool companies)
    {
        var previous = companies ? _companyCancellation : _addressCancellation;
        previous?.Cancel();
        var cancellation = new CancellationTokenSource();
        if (companies) { _companyCancellation = cancellation; CompanySuggestions = []; }
        else { _addressCancellation = cancellation; AddressSuggestions = []; }
        var query = companies ? CompanyQuery : Address;
        try
        {
            if (_disposed || query.Trim().Length < 3 || (companies && Kind != ClientKind.Professional)) return;
            await Task.Delay(350, cancellation.Token);
            if (companies)
            {
                var results = await _lookup.SearchCompaniesAsync(query, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (Kind == ClientKind.Professional) CompanySuggestions = results;
            }
            else
            {
                var results = await _lookup.SearchAddressesAsync(query, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                AddressSuggestions = results;
            }
            LookupStatus = string.Empty;
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested) LookupStatus = "Suggestions indisponibles. La saisie manuelle reste possible.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            if (!cancellation.IsCancellationRequested) LookupStatus = "Suggestions indisponibles. La saisie manuelle reste possible.";
        }
        finally { cancellation.Dispose(); if (companies && ReferenceEquals(_companyCancellation, cancellation)) _companyCancellation = null; if (!companies && ReferenceEquals(_addressCancellation, cancellation)) _addressCancellation = null; }
    }

    public void ApplyCompanySuggestion(CompanySuggestion suggestion)
    {
        if (Kind != ClientKind.Professional) return;
        _companyCancellation?.Cancel();
        _addressCancellation?.Cancel();
        _applyingSuggestion = true;
        try { ClientName = suggestion.Name; Siret = suggestion.Siret ?? string.Empty; if (!string.IsNullOrWhiteSpace(suggestion.Address)) Address = suggestion.Address; }
        finally { _applyingSuggestion = false; }
        CompanySuggestions = [];
        AddressSuggestions = [];
    }

    public void ApplyAddressSuggestion(AddressSuggestion suggestion)
    {
        _addressCancellation?.Cancel();
        _applyingSuggestion = true;
        try { Address = suggestion.Label; } finally { _applyingSuggestion = false; }
        AddressSuggestions = [];
    }

    public void Dispose() { _disposed = true; _companyCancellation?.Cancel(); _addressCancellation?.Cancel(); }
    public string Notes { get; set; } = string.Empty;
    public string ContactFirstName { get; set; } = string.Empty;
    public string ContactLastName { get; set; } = string.Empty;
    public string ContactRole { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    public string IdentityName => Kind == ClientKind.Professional
        ? ClientName
        : string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string NormalizedName => _existingFolderName ?? NameNormalizer.Normalize(IdentityName);

    private void RefreshIdentity()
    {
        OnPropertyChanged(nameof(IdentityName));
        OnPropertyChanged(nameof(NormalizedName));
        if (_codeEdited) return;
        _clientCode = ClientCodeSuggester.Suggest(IdentityName);
        OnPropertyChanged(nameof(ClientCode));
    }

    public ClientProfile CreateProfile()
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientProfile(
            Guid.NewGuid(), Kind, NormalizedName, ClientCode,
            Kind == ClientKind.Professional ? ClientName : null,
            Kind == ClientKind.Individual ? FirstName : null,
            Kind == ClientKind.Individual ? LastName : null,
            NullIfEmpty(Address), NullIfEmpty(Notes),
            new PrimaryContact(Kind == ClientKind.Professional ? NullIfEmpty(ContactFirstName) : null, Kind == ClientKind.Professional ? NullIfEmpty(ContactLastName) : null, Kind == ClientKind.Professional ? NullIfEmpty(ContactRole) : null, NullIfEmpty(ContactPhone), NullIfEmpty(ContactEmail)),
            now, now, Kind == ClientKind.Professional ? NullIfEmpty(Siret) : null);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

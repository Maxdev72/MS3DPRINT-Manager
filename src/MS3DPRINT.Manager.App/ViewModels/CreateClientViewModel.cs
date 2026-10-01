using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CreateClientViewModel : ObservableObject
{
    private readonly string? _existingFolderName;
    private string _clientName = string.Empty;
    private string _clientCode = string.Empty;
    private bool _codeEdited;
    private ClientKind _kind = ClientKind.Professional;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;

    public CreateClientViewModel() { }

    public CreateClientViewModel(string existingFolderName, string? existingClientCode)
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
        }
    }

    public ClientKind Kind
    {
        get => _kind;
        set
        {
            if (!SetProperty(ref _kind, value)) return;
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

    public string Address { get; set; } = string.Empty;
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
            new PrimaryContact(NullIfEmpty(ContactFirstName), NullIfEmpty(ContactLastName), NullIfEmpty(ContactRole), NullIfEmpty(ContactPhone), NullIfEmpty(ContactEmail)),
            now, now);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

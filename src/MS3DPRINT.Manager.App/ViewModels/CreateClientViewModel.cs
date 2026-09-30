using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CreateClientViewModel : ObservableObject
{
    private string _clientName = string.Empty;
    private string _clientCode = string.Empty;
    private bool _codeEdited;
    private ClientKind _kind = ClientKind.Professional;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;

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

    public string IdentityName => Kind == ClientKind.Professional
        ? ClientName
        : string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string NormalizedName => NameNormalizer.Normalize(IdentityName);

    private void RefreshIdentity()
    {
        OnPropertyChanged(nameof(IdentityName));
        OnPropertyChanged(nameof(NormalizedName));
        if (_codeEdited) return;
        _clientCode = ClientCodeSuggester.Suggest(IdentityName);
        OnPropertyChanged(nameof(ClientCode));
    }
}

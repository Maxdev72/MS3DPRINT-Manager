using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CreateClientViewModel : ObservableObject
{
    private string _clientName = string.Empty;
    private string _clientCode = string.Empty;
    private bool _codeEdited;

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

    public string NormalizedName => NameNormalizer.Normalize(ClientName);

    public string ClientCode
    {
        get => _clientCode;
        set
        {
            if (SetProperty(ref _clientCode, value)) _codeEdited = true;
        }
    }
}

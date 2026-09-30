using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ClientDetailViewModel : ObservableObject
{
    private readonly ClientProfileStore _store;
    private readonly ClientProfile _profile;
    private string? _notes;

    public ClientDetailViewModel(ClientProfile profile, ClientProfileStore store)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _notes = profile.Notes;
    }

    public string DisplayName => _profile.Kind == ClientKind.Professional
        ? _profile.CompanyName!
        : string.Join(" ", new[] { _profile.FirstName, _profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string ClientCode => _profile.ClientCode;
    public string? Address => _profile.Address;
    public PrimaryContact PrimaryContact => _profile.PrimaryContact;

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public void Save()
    {
        _store.Update(_profile with { Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(), UpdatedAt = DateTimeOffset.UtcNow });
    }
}

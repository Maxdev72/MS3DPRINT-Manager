using System.Collections.ObjectModel;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CreateProjectViewModel : ObservableObject
{
    private readonly string _clientsRoot;
    private readonly ClientCodeRegistry _registry;
    private string? _selectedClient;
    private string _clientCode = string.Empty;
    private string _projectName = string.Empty;
    private string _year = DateTime.Now.Year.ToString();
    private bool _codeRegistered;
    private string? _codeLoadError;
    private string? _referenceOverride;

    public CreateProjectViewModel(string storageRoot, ClientCodeRegistry registry)
    {
        _clientsRoot = Path.Combine(storageRoot, "01_CLIENTS");
        _registry = registry;
        RefreshClients();
    }

    public ObservableCollection<string> Clients { get; } = new();

    public string? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (!SetProperty(ref _selectedClient, value)) return;
            _codeLoadError = null;
            try { _clientCode = value is null ? string.Empty : _registry.GetCode(value) ?? string.Empty; }
            catch (Exception exception)
            {
                _clientCode = string.Empty;
                _codeLoadError = UiErrorMessages.For(exception);
            }
            _codeRegistered = _clientCode.Length > 0;
            OnPropertyChanged(nameof(ClientCode));
            OnPropertyChanged(nameof(CodeNeedsSaving));
            OnPropertyChanged(nameof(CodeIsReadOnly));
            OnPropertyChanged(nameof(CodeLoadError));
            OnPropertyChanged(nameof(ReferencePreview));
        }
    }

    public string ClientCode
    {
        get => _clientCode;
        set
        {
            if (!SetProperty(ref _clientCode, value)) return;
            OnPropertyChanged(nameof(ReferencePreview));
        }
    }

    public bool CodeNeedsSaving => SelectedClient is not null && !_codeRegistered;
    public bool CodeIsReadOnly => _codeRegistered;
    public string? CodeLoadError => _codeLoadError;

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (!SetProperty(ref _projectName, value)) return;
            OnPropertyChanged(nameof(NormalizedName));
            OnPropertyChanged(nameof(ReferencePreview));
        }
    }

    public string NormalizedName => NameNormalizer.Normalize(ProjectName);

    public string Year
    {
        get => _year;
        set
        {
            if (!SetProperty(ref _year, value)) return;
            OnPropertyChanged(nameof(ReferencePreview));
        }
    }

    public string ReferencePreview
    {
        get
        {
            if (_referenceOverride is not null) return _referenceOverride;
            if (_codeLoadError is not null) return _codeLoadError;
            if (SelectedClient is null || string.IsNullOrEmpty(NormalizedName) || string.IsNullOrEmpty(NameNormalizer.Normalize(ClientCode)) ||
                !int.TryParse(Year, out var year) || year is < 1000 or > 9999)
                return "Sélectionnez un client et renseignez un code, une année et un nom valides.";

            try { return CreateReference().FolderName; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { return "Aperçu indisponible : " + UiErrorMessages.For(exception); }
        }
    }

    public void FreezeReferencePreview(string folderName)
    {
        _referenceOverride = folderName;
        OnPropertyChanged(nameof(ReferencePreview));
    }

    public string ClientPath => SelectedClient is null
        ? throw new ArgumentException("Sélectionnez un client.")
        : Path.Combine(_clientsRoot, SelectedClient);

    public ProjectReference CreateReference()
    {
        if (_codeLoadError is not null) throw new InvalidOperationException(_codeLoadError);
        if (SelectedClient is null) throw new ArgumentException("Sélectionnez un client.");
        if (!Directory.Exists(ClientPath)) throw new DirectoryNotFoundException("Le dossier du client sélectionné est introuvable.");
        if (!int.TryParse(Year, out var year) || year is < 1000 or > 9999)
            throw new ArgumentException("L’année doit comporter quatre chiffres.");
        if (NameNormalizer.Normalize(ClientCode).Length == 0)
            throw new ArgumentException("Saisissez un code client contenant au moins une lettre ou un chiffre.");
        if (NormalizedName.Length == 0)
            throw new ArgumentException("Saisissez un nom de projet contenant au moins une lettre ou un chiffre.");
        var existingNames = Directory.EnumerateDirectories(ClientPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).OfType<string>();
        return ProjectReferenceGenerator.Create(ClientCode, year, existingNames, ProjectName);
    }

    public void RefreshClients()
    {
        Clients.Clear();
        if (!Directory.Exists(_clientsRoot)) return;
        foreach (var path in Directory.EnumerateDirectories(_clientsRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            Clients.Add(Path.GetFileName(path));
        }
    }
}

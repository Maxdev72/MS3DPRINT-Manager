namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string DefaultStorageRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
    private string _status = "Prêt.";

    public MainViewModel(string? storageRoot = null)
    {
        StorageRoot = string.IsNullOrWhiteSpace(storageRoot) ? DefaultStorageRoot : Path.GetFullPath(storageRoot);
    }

    public string StorageRoot { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string DefaultStorageRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
    private string _status = "Prêt.";
    private string _dashboardClientsCount = "—";
    private string _dashboardProjectsCount = "—";
    private string _dashboardQuotesCount = "—";
    private string _dashboardInProgressCount = "—";
    private string _dashboardCompletedCount = "—";

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

    public string DashboardClientsCount
    {
        get => _dashboardClientsCount;
        private set => SetProperty(ref _dashboardClientsCount, value);
    }

    public string DashboardProjectsCount
    {
        get => _dashboardProjectsCount;
        private set => SetProperty(ref _dashboardProjectsCount, value);
    }

    public string DashboardQuotesCount
    {
        get => _dashboardQuotesCount;
        private set => SetProperty(ref _dashboardQuotesCount, value);
    }

    public string DashboardInProgressCount
    {
        get => _dashboardInProgressCount;
        private set => SetProperty(ref _dashboardInProgressCount, value);
    }

    public string DashboardCompletedCount
    {
        get => _dashboardCompletedCount;
        private set => SetProperty(ref _dashboardCompletedCount, value);
    }

    public void ApplyDashboard(DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DashboardClientsCount = snapshot.Clients.ToString();
        DashboardProjectsCount = snapshot.Projects.ToString();
        DashboardQuotesCount = snapshot.Quotes.ToString();
        DashboardInProgressCount = snapshot.InProgress.ToString();
        DashboardCompletedCount = snapshot.Completed.ToString();
    }
}

using System.Windows;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Search;

namespace MS3DPRINT.Manager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FolderTreeService _folders = new();
    private readonly ClientCodeRegistry _codes;
    private readonly ThemeSettingsStore _themeSettings;
    private readonly ClientProfileStore _clientProfiles;
    private readonly ClientCatalog _clientCatalog;
    private readonly ProjectProfileStore _projectProfiles;
    private readonly ProjectCatalog _projectCatalog;
    private readonly CollectionCatalog _collectionCatalog = new();
    private int _dashboardRefreshVersion;
    private StorageChangeWatcher? _storageWatcher;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(Environment.GetEnvironmentVariable("MS3DPRINT_STORAGE_ROOT"));
        var dataDirectory = Environment.GetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY");
        _codes = new ClientCodeRegistry(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory);
        _themeSettings = new ThemeSettingsStore(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory);
        _clientProfiles = new ClientProfileStore(new WorkspaceMetadataPaths(_viewModel.StorageRoot));
        _clientCatalog = new ClientCatalog(_clientProfiles, _codes);
        _projectProfiles = new ProjectProfileStore(new WorkspaceMetadataPaths(_viewModel.StorageRoot));
        _projectCatalog = new ProjectCatalog(_projectProfiles);
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            StartStorageWatcher();
            await RefreshDashboardSafelyAsync();
        };
        Closed += (_, _) => { _closed = true; _storageWatcher?.Dispose(); _storageWatcher = null; ++_dashboardRefreshVersion; };
        Width = Math.Min(1200, SystemParameters.WorkArea.Width * 0.84);
        Height = Math.Min(850, SystemParameters.WorkArea.Height * 0.85);
    }

    private void VerifyStructure_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var result = _folders.EnsureMainStructure(_viewModel.StorageRoot);
        _viewModel.Status = result.CreatedFolders.Count == 0
            ? "Arborescence vérifiée : tous les dossiers principaux existent."
            : $"Arborescence vérifiée : {result.CreatedFolders.Count} dossier(s) ajouté(s).";
    });

    private void NewClient_Click(object sender, RoutedEventArgs e) => Run(() =>
        ShowDialog(new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles) { Owner = this }));

    private async void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        PageHost.Content = DashboardPage;
        await RefreshDashboardSafelyAsync();
    }

    private async void DashboardRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDashboardSafelyAsync();

    private void Clients_Click(object sender, RoutedEventArgs e) => ShowClients();
    private void Search_Click(object sender, RoutedEventArgs e) => ShowSearch();
    private void Projects_Click(object sender, RoutedEventArgs e) => ShowProjects();
    private void Models_Click(object sender, RoutedEventArgs e) => ShowCollection("Modèles 3D", "Retrouver vos modèles et leurs fichiers de conception.", "02_MODELES_3D", FolderTemplates.Model, "Nouveau modèle 3D");
    private void Products_Click(object sender, RoutedEventArgs e) => ShowCollection("Produits", "Suivre les produits et leur documentation de fabrication.", "03_PRODUITS_MS3DPRINT", FolderTemplates.Product, "Nouveau produit MS3DPRINT");
    private void Suppliers_Click(object sender, RoutedEventArgs e) => ShowCollection("Fournisseurs", "Centraliser les dossiers fournisseurs, tarifs et commandes.", "06_FOURNISSEURS", FolderTemplates.Supplier, "Nouveau fournisseur");

    private void ShowSearch()
    {
        var page = new SearchView(new GlobalSearchService(_clientCatalog, _projectCatalog), _viewModel.StorageRoot);
        void ReturnToSearch() => PageHost.Content = page;
        page.ResultSelected += result => Run(() =>
        {
            switch (result.Kind)
            {
                case GlobalSearchResultKind.Client when result.Client is not null:
                    ShowClientDetail(result.Client, ReturnToSearch);
                    break;
                case GlobalSearchResultKind.Project when result.Project is not null:
                    ShowProjectDetail(result.Project, ReturnToSearch);
                    break;
                case GlobalSearchResultKind.File:
                    switch (FileOpenRouting.Decide(result.Path))
                    {
                        case FileOpenTarget.ThreeD:
                            ModelPreviewLauncher.Show(result.Path, this);
                            break;
                        case FileOpenTarget.Document:
                            new DocumentPreviewWindow(result.Path) { Owner = this }.ShowDialog();
                            break;
                        default:
                            ExplorerService.Open(result.Path);
                            break;
                    }
                    break;
            }
        });
        PageHost.Content = page;
    }

    private void ShowClients()
    {
        var page = new ClientsView(new ClientsViewModel(_clientCatalog, _viewModel.StorageRoot));
        page.CreateRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles) { Owner = this });
        });
        page.ClientSelected += client => ShowClientDetail(client);
        PageHost.Content = page;
    }

    private void ShowClientDetail(ClientSummary client, Action? backRequested = null)
        => Run(() => ShowClientDetailCore(client, backRequested));

    private void ShowClientDetailCore(ClientSummary client, Action? backRequested)
    {
        var navigateBack = backRequested ?? ShowClients;
        if (client.Profile is null)
        {
            Run(() =>
            {
                ShowDialog(new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles, client) { Owner = this });
                navigateBack();
            });
            return;
        }
        var profile = _clientProfiles.Load(client.Profile.Id);
        client = client with
        {
            Profile = profile,
            DisplayName = profile.Kind == ClientKind.Professional
                ? profile.CompanyName!
                : string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)))
        };
        var page = new ClientDetailView(new ClientDetailViewModel(profile, _clientProfiles, _projectCatalog, _viewModel.StorageRoot));
        page.BackRequested += (_, _) => navigateBack();
        page.CreateProjectRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles, preselectedClient: client) { Owner = this });
        });
        page.ProjectSelected += project => ShowProjectDetail(project, () => ShowClientDetail(client, navigateBack));
        PageHost.Content = page;
    }

    private void ShowProjects()
    {
        var page = new ProjectsView(new ProjectsViewModel(_projectCatalog, _viewModel.StorageRoot));
        page.CreateRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles) { Owner = this });
        });
        page.ProjectSelected += project => ShowProjectDetail(project);
        PageHost.Content = page;
    }

    private void ShowCollection(string title, string subtitle, string parentFolder, IReadOnlyList<string> template, string createTitle)
    {
        var page = new CollectionView(new CollectionViewModel(_collectionCatalog, _viewModel.StorageRoot, parentFolder, title, subtitle));
        page.BackRequested += (_, _) => PageHost.Content = DashboardPage;
        page.CreateRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateNamedItemWindow(createTitle, Path.Combine(_viewModel.StorageRoot, parentFolder), template, _folders) { Owner = this });
        });
        page.ItemSelected += item => ShowCollectionDetail(item, () => ShowCollection(title, subtitle, parentFolder, template, createTitle));
        PageHost.Content = page;
    }

    private void ShowCollectionDetail(CollectionItemSummary item, Action backRequested)
    {
        var page = new CollectionDetailView(new CollectionDetailViewModel(item));
        page.BackRequested += (_, _) => backRequested();
        PageHost.Content = page;
    }

    private void ShowProjectDetail(ProjectSummary project, Action? backRequested = null)
    {
        var navigateBack = backRequested ?? ShowProjects;
        if (project.Profile is null)
        {
            Run(() =>
            {
                ShowDialog(new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles, project) { Owner = this });
                navigateBack();
            });
            return;
        }
        var page = new ProjectDetailView(new ProjectDetailViewModel(project, _projectProfiles));
        page.BackRequested += (_, _) => navigateBack();
        page.ClassifyRequested += (_, _) => Run(() => ShowDialog(new ClassifyFileWindow(_viewModel.StorageRoot, project.ProjectPath) { Owner = this }));
        PageHost.Content = page;
    }

    private void NewProject_Click(object sender, RoutedEventArgs e) => Run(() =>
        ShowDialog(new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles) { Owner = this }));

    private void NewModel_Click(object sender, RoutedEventArgs e) => ShowNamedItem("Nouveau modèle 3D", "02_MODELES_3D", FolderTemplates.Model);

    private void NewProduct_Click(object sender, RoutedEventArgs e) => ShowNamedItem("Nouveau produit MS3DPRINT", "03_PRODUITS_MS3DPRINT", FolderTemplates.Product);

    private void NewSupplier_Click(object sender, RoutedEventArgs e) => ShowNamedItem("Nouveau fournisseur", "06_FOURNISSEURS", FolderTemplates.Supplier);

    private void OpenRoot_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _folders.EnsureMainStructure(_viewModel.StorageRoot);
        ExplorerService.Open(_viewModel.StorageRoot);
        _viewModel.Status = "Dossier MS3DPRINT ouvert dans l’Explorateur.";
    });

    private void ClassifyFile_Click(object sender, RoutedEventArgs e) => Run(() =>
        ShowDialog(new ClassifyFileWindow(_viewModel.StorageRoot) { Owner = this }));

    private void Open3DViewer_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        ModelPreviewLauncher.Show(null, this);
    });

    private void Settings_Click(object sender, RoutedEventArgs e) => Run(() =>
        new SettingsWindow(_themeSettings, _viewModel.StorageRoot) { Owner = this }.ShowDialog());

    private void ShowNamedItem(string title, string parentFolder, IReadOnlyList<string> template)
        => Run(() => ShowDialog(new CreateNamedItemWindow(title, Path.Combine(_viewModel.StorageRoot, parentFolder), template, _folders) { Owner = this }));

    private void ShowDialog(Window dialog)
    {
        if (dialog.ShowDialog() == true)
        {
            _viewModel.Status = "Élément traité : " + ((ICreatedFolderDialog)dialog).CreatedPath;
            _ = RefreshVisibleAsync();
        }
    }

    private void StartStorageWatcher()
    {
        if (_closed || _storageWatcher is not null || !Directory.Exists(_viewModel.StorageRoot)) return;
        try
        {
            _storageWatcher = new StorageChangeWatcher(_viewModel.StorageRoot,
                () => Dispatcher.BeginInvoke(new Action(() => _ = RefreshVisibleAsync())),
                exception => Dispatcher.BeginInvoke(new Action(() => _viewModel.Status = "Actualisation automatique indisponible : " + UiErrorMessages.For(exception))));
        }
        catch (Exception exception) { _viewModel.Status = "Actualisation automatique indisponible : " + UiErrorMessages.For(exception); }
    }

    private async Task RefreshVisibleAsync()
    {
        if (_closed) return;
        StartStorageWatcher();
        var page = PageHost.Content;
        var scrollPositions = page is DependencyObject visual ? FindScrollViewers(visual).Select(scroll => (Scroll: scroll, Offset: scroll.VerticalOffset)).ToArray() : [];
        var refresh = page switch
        {
            ClientsView view => view.RefreshAsync(), ProjectsView view => view.RefreshAsync(),
            CollectionView view => view.RefreshAsync(), ClientDetailView view => view.RefreshAsync(),
            ProjectDetailView view => view.RefreshAsync(), CollectionDetailView view => view.RefreshAsync(),
            _ => Task.CompletedTask
        };
        await Task.WhenAll(refresh, RefreshDashboardSafelyAsync());
        if (ReferenceEquals(page, PageHost.Content))
            foreach (var position in scrollPositions) position.Scroll.ScrollToVerticalOffset(position.Offset);
    }

    private static IEnumerable<System.Windows.Controls.ScrollViewer> FindScrollViewers(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer scroll) yield return scroll;
            foreach (var descendant in FindScrollViewers(child)) yield return descendant;
        }
    }

    private void Run(Action action)
    {
        try { action(); StartStorageWatcher(); }
        catch (Exception exception)
        {
            _viewModel.Status = UiErrorMessages.For(exception);
            MessageBox.Show(this, _viewModel.Status, "MS3DPRINT — erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshDashboardSafelyAsync()
    {
        var refreshVersion = ++_dashboardRefreshVersion;
        DashboardLoadingText.Visibility = Visibility.Visible;
        DashboardErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var snapshot = await Task.Run(LoadDashboardSnapshot);
            if (refreshVersion != _dashboardRefreshVersion) return;
            _viewModel.ApplyDashboard(snapshot);
            DashboardErrorText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            if (refreshVersion != _dashboardRefreshVersion) return;
            DashboardErrorText.Text = UiErrorMessages.For(exception);
            DashboardErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (refreshVersion == _dashboardRefreshVersion) DashboardLoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private DashboardSnapshot LoadDashboardSnapshot()
        => DashboardSnapshot.Create(_clientCatalog.Load(_viewModel.StorageRoot), _projectCatalog.Load(_viewModel.StorageRoot));
}

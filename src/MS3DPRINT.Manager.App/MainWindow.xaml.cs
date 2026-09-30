using System.Windows;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Projects;

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

    private void Dashboard_Click(object sender, RoutedEventArgs e) => PageHost.Content = DashboardPage;

    private void Clients_Click(object sender, RoutedEventArgs e) => ShowClients();
    private void Projects_Click(object sender, RoutedEventArgs e) => ShowProjects();

    private void ShowClients()
    {
        var page = new ClientsView(new ClientsViewModel(_clientCatalog, _viewModel.StorageRoot));
        page.CreateRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles) { Owner = this });
            ShowClients();
        });
        page.ClientSelected += ShowClientDetail;
        PageHost.Content = page;
    }

    private void ShowClientDetail(ClientSummary client)
    {
        if (client.Profile is null)
        {
            _viewModel.Status = "Ce client existe déjà, mais sa fiche reste à compléter.";
            return;
        }
        var page = new ClientDetailView(new ClientDetailViewModel(client.Profile, _clientProfiles));
        page.BackRequested += (_, _) => ShowClients();
        PageHost.Content = page;
    }

    private void ShowProjects()
    {
        var page = new ProjectsView(new ProjectsViewModel(_projectCatalog, _viewModel.StorageRoot));
        page.CreateRequested += (_, _) => Run(() =>
        {
            ShowDialog(new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles) { Owner = this });
            ShowProjects();
        });
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

    private void Settings_Click(object sender, RoutedEventArgs e) => Run(() =>
        new SettingsWindow(_themeSettings, _viewModel.StorageRoot) { Owner = this }.ShowDialog());

    private void ShowNamedItem(string title, string parentFolder, IReadOnlyList<string> template)
        => Run(() => ShowDialog(new CreateNamedItemWindow(title, Path.Combine(_viewModel.StorageRoot, parentFolder), template, _folders) { Owner = this }));

    private void ShowDialog(Window dialog)
    {
        if (dialog.ShowDialog() == true) _viewModel.Status = "Élément traité : " + ((ICreatedFolderDialog)dialog).CreatedPath;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            _viewModel.Status = UiErrorMessages.For(exception);
            MessageBox.Show(this, _viewModel.Status, "MS3DPRINT — erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateTrackedProjectWindow : Window, ICreatedFolderDialog
{
    private readonly string _storageRoot;
    private readonly ClientCatalog _clients;
    private readonly ProjectProfileStore _profiles;
    private readonly ProjectCreationService _projects;
    private readonly ProjectSummary? _existingProject;
    private readonly ClientSummary? _preselectedClient;

    public CreateTrackedProjectWindow(string storageRoot, FolderTreeService folders, ClientCatalog clients, ProjectProfileStore profiles, ProjectSummary? existingProject = null, ClientSummary? preselectedClient = null)
    {
        InitializeComponent();
        _storageRoot = Path.GetFullPath(storageRoot);
        _clients = clients;
        _profiles = profiles;
        _projects = new ProjectCreationService(folders);
        _existingProject = existingProject;
        _preselectedClient = preselectedClient;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += (_, _) => LoadClientsSafely();
        ClientBox.SelectionChanged += Client_SelectionChanged;
        YearBox.TextChanged += (_, _) => UpdateCreateState();
        ProjectNameBox.TextChanged += (_, _) => UpdateCreateState();
        if (existingProject is not null)
        {
            Title = "Compléter la fiche projet";
            Heading.Text = "Compléter la fiche projet";
            IntroText.Text = $"Le dossier {existingProject.FolderName} et sa référence existante ne seront pas modifiés.";
            CreateButton.Content = "Enregistrer la fiche";
        }
    }

    public string? CreatedPath { get; private set; }

    private void Retry_Click(object sender, RoutedEventArgs e) => LoadClientsSafely();

    private void LoadClientsSafely()
    {
        ErrorText.Text = string.Empty;
        RetryButton.Visibility = Visibility.Collapsed;
        ClientBox.IsEnabled = true;
        try
        {
            var clients = _clients.Load(_storageRoot).Where(client => client.Profile is not null);
            if (_existingProject is not null) clients = clients.Where(client => string.Equals(client.FolderName, _existingProject.ClientFolderName, StringComparison.OrdinalIgnoreCase));
            ClientBox.ItemsSource = clients.ToArray();
            NoClientText.Visibility = ClientBox.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            YearBox.Text = _existingProject is null ? DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) : GetYear(_existingProject.Reference).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_existingProject is not null)
            {
                ProjectNameBox.Text = _existingProject.ProjectName;
                YearBox.IsReadOnly = true;
                ProjectNameBox.IsReadOnly = true;
            }
            if (ClientBox.Items.Count > 0 && (_preselectedClient is not null || _existingProject is not null))
            {
                ClientBox.SelectedItem = _preselectedClient is null
                    ? ClientBox.Items[0]
                    : clients.FirstOrDefault(client => string.Equals(client.FolderName, _preselectedClient.FolderName, StringComparison.OrdinalIgnoreCase));
            }
            else if (ClientBox.Items.Count == 0) ClientBox.IsEnabled = false;
            UpdateCreateState();
            if (ClientBox.SelectedItem is null && ClientBox.IsEnabled) ClientBox.Focus();
            else ProjectNameBox.Focus();
        }
        catch (Exception exception)
        {
            ClientBox.ItemsSource = Array.Empty<ClientSummary>();
            ClientBox.IsEnabled = false;
            NoClientText.Visibility = Visibility.Collapsed;
            CreateButton.IsEnabled = false;
            ErrorText.Text = UiErrorMessages.For(exception);
            RetryButton.Visibility = Visibility.Visible;
        }
    }

    private void Client_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClientCodeBox.Text = (ClientBox.SelectedItem as ClientSummary)?.ClientCode ?? string.Empty;
        UpdateCreateState();
    }

    private void UpdateCreateState()
    {
        CreateButton.IsEnabled = ClientBox.SelectedItem is ClientSummary &&
            int.TryParse(YearBox.Text, out var year) && year is >= 2000 and <= 9999 &&
            NameNormalizer.Normalize(ProjectNameBox.Text ?? string.Empty).Length > 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (ClientBox.SelectedItem is not ClientSummary client)
                throw new InvalidOperationException("Sélectionnez un client avec une fiche complète.");
            if (!int.TryParse(YearBox.Text, out var year) || year is < 2000 or > 9999)
                throw new InvalidOperationException("Saisissez une année valide.");
            if (string.IsNullOrWhiteSpace(ProjectNameBox.Text))
                throw new InvalidOperationException("Saisissez le nom du projet.");

            DateOnly? dueDate = DueDateBox.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;
            if (_existingProject is null)
            {
                var result = _projects.CreateWithProfile(client, year, ProjectNameBox.Text, _profiles, dueDate, DescriptionBox.Text);
                CreatedPath = Path.Combine(client.ClientPath, result.Reference.FolderName);
            }
            else
            {
                if (!Directory.Exists(_existingProject.ProjectPath)) throw new DirectoryNotFoundException("Le dossier projet à compléter est introuvable.");
                if (client.Profile is null) throw new InvalidOperationException("Complétez la fiche client avant de compléter ce projet.");
                var now = DateTimeOffset.UtcNow;
                var projectCode = _existingProject.Reference.Split('-', 2)[0];
                _profiles.Create(new ProjectProfile(Guid.NewGuid(), client.Profile.Id, projectCode, _existingProject.Reference,
                    _existingProject.FolderName, _existingProject.ProjectName, ProjectStatus.Quote, now, dueDate,
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(), null, now));
                CreatedPath = _existingProject.ProjectPath;
            }
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }

    private static int GetYear(string reference)
    {
        var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && int.TryParse(parts[1], out var year) ? year : DateTime.Today.Year;
    }
}

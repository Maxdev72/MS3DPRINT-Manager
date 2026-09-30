using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateTrackedProjectWindow : Window, ICreatedFolderDialog
{
    private readonly string _storageRoot;
    private readonly ClientCatalog _clients;
    private readonly ProjectProfileStore _profiles;
    private readonly ProjectCreationService _projects;

    public CreateTrackedProjectWindow(string storageRoot, FolderTreeService folders, ClientCatalog clients, ProjectProfileStore profiles)
    {
        InitializeComponent();
        _storageRoot = Path.GetFullPath(storageRoot);
        _clients = clients;
        _profiles = profiles;
        _projects = new ProjectCreationService(folders);
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += OnLoaded;
        ClientBox.SelectionChanged += Client_SelectionChanged;
    }

    public string? CreatedPath { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClientBox.ItemsSource = _clients.Load(_storageRoot).Where(client => client.Profile is not null).ToArray();
        NoClientText.Visibility = ClientBox.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        YearBox.Text = DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ClientBox.Items.Count > 0) ClientBox.SelectedIndex = 0;
        else ClientBox.IsEnabled = false;
        ProjectNameBox.Focus();
    }

    private void Client_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClientCodeBox.Text = (ClientBox.SelectedItem as ClientSummary)?.ClientCode ?? string.Empty;
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
            var result = _projects.CreateWithProfile(client, year, ProjectNameBox.Text, _profiles, dueDate, DescriptionBox.Text);
            CreatedPath = Path.Combine(client.ClientPath, result.Reference.FolderName);
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}

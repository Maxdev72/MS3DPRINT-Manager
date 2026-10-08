using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.App.Clients;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateClientWindow : Window, ICreatedFolderDialog
{
    private readonly string _storageRoot;
    private readonly ClientCreationService _clients;
    private readonly ClientProfileStore _profiles;
    private readonly ClientSummary? _existingClient;
    private readonly CreateClientViewModel _viewModel;

    public CreateClientWindow(string storageRoot, FolderTreeService folders, ClientCodeRegistry registry, ClientProfileStore profiles, ClientSummary? existingClient = null)
    {
        _existingClient = existingClient;
        _viewModel = existingClient is null
            ? new CreateClientViewModel()
            : new CreateClientViewModel(existingClient.FolderName, existingClient.ClientCode);
        if (existingClient is not null) _viewModel.ClientName = existingClient.DisplayName;
        InitializeComponent();
        KindBox.SelectionChanged += Kind_SelectionChanged;
        _storageRoot = Path.GetFullPath(storageRoot);
        _clients = new ClientCreationService(_storageRoot, folders, profiles, registry);
        _profiles = profiles;
        DataContext = _viewModel;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        if (existingClient is not null)
        {
            Title = "Compléter la fiche client";
            Heading.Text = "Compléter la fiche client";
            IntroText.Text = $"Le dossier {existingClient.FolderName} et ses fichiers existants ne seront pas modifiés.";
            FolderHint.Text = "Ce dossier existant est conservé tel quel.";
            CreateButton.Content = "Enregistrer la fiche";
        }
        Loaded += (_, _) => CompanyBox.Focus();
        Closed += (_, _) => _viewModel.Dispose();
    }

    public string? CreatedPath { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var individual = KindBox.SelectedIndex == 1;
        _viewModel.Kind = individual ? ClientKind.Individual : ClientKind.Professional;
        ProfessionalPanel.Visibility = individual ? Visibility.Collapsed : Visibility.Visible;
        IndividualPanel.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
        ContactIdentityPanel.Visibility = individual ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CompanySuggestion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ListBox)sender).SelectedItem is CompanySuggestion suggestion) _viewModel.ApplyCompanySuggestion(suggestion);
    }

    private void AddressSuggestion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ListBox)sender).SelectedItem is AddressSuggestion suggestion) _viewModel.ApplyAddressSuggestion(suggestion);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            ClientProfile profile;
            if (_existingClient is null)
            {
                profile = _clients.Create(_viewModel.CreateProfile());
                CreatedPath = Path.Combine(_storageRoot, "01_CLIENTS", profile.FolderName);
            }
            else
            {
                if (!Directory.Exists(_existingClient.ClientPath)) throw new DirectoryNotFoundException("Le dossier client à compléter est introuvable.");
                profile = _viewModel.CreateProfile() with { RelativePath = Path.GetRelativePath(_storageRoot, _existingClient.ClientPath) };
                _profiles.Create(profile);
                CreatedPath = _existingClient.ClientPath;
            }
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}

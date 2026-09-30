using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateClientWindow : Window, ICreatedFolderDialog
{
    private readonly string _storageRoot;
    private readonly ClientCreationService _clients;
    private readonly CreateClientViewModel _viewModel = new();

    public CreateClientWindow(string storageRoot, FolderTreeService folders, ClientCodeRegistry registry, ClientProfileStore profiles)
    {
        InitializeComponent();
        _storageRoot = Path.GetFullPath(storageRoot);
        _clients = new ClientCreationService(_storageRoot, folders, profiles, registry);
        DataContext = _viewModel;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += (_, _) => CompanyBox.Focus();
    }

    public string? CreatedPath { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var individual = KindBox.SelectedIndex == 1;
        _viewModel.Kind = individual ? ClientKind.Individual : ClientKind.Professional;
        ProfessionalPanel.Visibility = individual ? Visibility.Collapsed : Visibility.Visible;
        IndividualPanel.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            var profile = _clients.Create(_viewModel.CreateProfile());
            CreatedPath = Path.Combine(_storageRoot, "01_CLIENTS", profile.FolderName);
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}

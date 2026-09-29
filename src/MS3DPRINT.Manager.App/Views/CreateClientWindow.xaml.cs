using System.Windows;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateClientWindow : Window, ICreatedFolderDialog
{
    private readonly string _storageRoot;
    private readonly FolderTreeService _folders;
    private readonly ClientCodeRegistry _registry;
    private readonly CreateClientViewModel _viewModel = new();

    public CreateClientWindow(string storageRoot, FolderTreeService folders, ClientCodeRegistry registry)
    {
        InitializeComponent();
        _storageRoot = storageRoot;
        _folders = folders;
        _registry = registry;
        DataContext = _viewModel;
        Loaded += (_, _) => NameBox.Focus();
    }

    public string? CreatedPath { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (_viewModel.NormalizedName.Length == 0)
        {
            ErrorText.Text = "Saisissez un nom contenant au moins une lettre ou un chiffre.";
            return;
        }
        if (NameNormalizer.Normalize(_viewModel.ClientCode).Length == 0)
        {
            ErrorText.Text = "Saisissez un code client contenant au moins une lettre ou un chiffre.";
            return;
        }

        try
        {
            if (_registry.GetCode(_viewModel.NormalizedName) is not null)
                throw new FolderConflictException("Un code est déjà enregistré pour ce client.");
            var destination = Path.Combine(_storageRoot, "01_CLIENTS", _viewModel.NormalizedName);
            CreatedPath = _folders.CreateTree(destination, FolderTemplates.Client).DestinationPath;
            try { _registry.Add(_viewModel.NormalizedName, _viewModel.ClientCode); }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Le dossier a été créé, mais son code n’a pas été enregistré. " + UiErrorMessages.For(exception),
                    "MS3DPRINT — code non enregistré", MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = true;
                return;
            }
            OfferOpenFolder();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = UiErrorMessages.For(exception);
        }
    }

    private void OfferOpenFolder()
    {
        if (MessageBox.Show(this, "Client créé. Ouvrir son dossier ?", "MS3DPRINT", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;
        try { ExplorerService.Open(CreatedPath!); }
        catch (Exception exception) { MessageBox.Show(this, UiErrorMessages.For(exception), "MS3DPRINT — Explorateur", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

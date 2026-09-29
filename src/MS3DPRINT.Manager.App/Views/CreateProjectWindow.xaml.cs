using System.Windows;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateProjectWindow : Window, ICreatedFolderDialog
{
    private readonly CreateProjectViewModel _viewModel;
    private readonly FolderTreeService _folders;
    private readonly ClientCodeRegistry _registry;

    public CreateProjectWindow(string storageRoot, FolderTreeService folders, ClientCodeRegistry registry)
    {
        InitializeComponent();
        _folders = folders;
        _registry = registry;
        _viewModel = new CreateProjectViewModel(storageRoot, registry);
        DataContext = _viewModel;
        Loaded += (_, _) => ClientBox.Focus();
    }

    public string? CreatedPath { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            var reference = _viewModel.CreateReference();
            if (_viewModel.CodeNeedsSaving)
            {
                var answer = MessageBox.Show(this,
                    $"Aucun code n’est enregistré pour {_viewModel.SelectedClient}. Enregistrer le code {reference.ClientCode} pour ce client ?",
                    "Confirmer le code client", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            CreatedPath = _folders.CreateTree(Path.Combine(_viewModel.ClientPath, reference.FolderName), FolderTemplates.Project).DestinationPath;
            if (_viewModel.CodeNeedsSaving)
            {
                try { _registry.Add(_viewModel.SelectedClient!, reference.ClientCode); }
                catch (Exception exception)
                {
                    MessageBox.Show(this, "Le projet a été créé, mais le code client n’a pas été enregistré. " + UiErrorMessages.For(exception),
                        "MS3DPRINT — code non enregistré", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DialogResult = true;
                    return;
                }
            }

            if (MessageBox.Show(this, "Projet créé. Ouvrir son dossier ?", "MS3DPRINT", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { ExplorerService.Open(CreatedPath); }
                catch (Exception exception) { MessageBox.Show(this, UiErrorMessages.For(exception), "MS3DPRINT — Explorateur", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}

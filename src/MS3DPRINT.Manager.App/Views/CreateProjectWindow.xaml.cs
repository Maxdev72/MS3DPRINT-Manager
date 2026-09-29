using System.ComponentModel;
using System.Windows;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateProjectWindow : Window, ICreatedFolderDialog
{
    private readonly CreateProjectViewModel _viewModel;
    private readonly ProjectCreationService _projects;
    private readonly ClientCodeRegistry _registry;
    private PendingCodeRegistration? _pendingRegistration;

    public CreateProjectWindow(string storageRoot, FolderTreeService folders, ClientCodeRegistry registry)
    {
        InitializeComponent();
        _projects = new ProjectCreationService(folders);
        _registry = registry;
        _viewModel = new CreateProjectViewModel(storageRoot, registry);
        DataContext = _viewModel;
        Loaded += (_, _) => ClientBox.Focus();
    }

    public string? CreatedPath { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_pendingRegistration?.IsPending == true)
            e.Cancel = MessageBox.Show(this,
                "Le dossier du projet existe déjà, mais le code client n’est pas enregistré. Fermer ce formulaire sans réessayer ?",
                "Code client en attente", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (CreatedPath is null)
            {
                var reference = _viewModel.CreateReference();
                var needsRegistration = _viewModel.CodeNeedsSaving;
                if (needsRegistration)
                {
                    var answer = MessageBox.Show(this,
                        $"Aucun code n’est enregistré pour {_viewModel.SelectedClient}. Enregistrer le code {reference.ClientCode} pour ce client ?",
                        "Confirmer le code client", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer != MessageBoxResult.Yes) return;
                }

                var createdReference = _projects.Create(_viewModel.ClientPath, reference.ClientCode, reference.Year, _viewModel.ProjectName);
                CreatedPath = Path.Combine(_viewModel.ClientPath, createdReference.FolderName);
                _pendingRegistration = needsRegistration
                    ? new PendingCodeRegistration(_viewModel.SelectedClient!, createdReference.ClientCode)
                    : null;
                if (_pendingRegistration is not null)
                {
                    _viewModel.FreezeReferencePreview(createdReference.FolderName);
                    ClientBox.IsEnabled = false;
                    CodeBox.IsEnabled = false;
                    YearBox.IsEnabled = false;
                    ProjectNameBox.IsEnabled = false;
                }
            }

            if (_pendingRegistration is not null)
            {
                try { _pendingRegistration.Complete(_registry); }
                catch (Exception exception)
                {
                    ErrorText.Text = "Le projet a été créé, mais le code client n’a pas été enregistré. Vous pouvez réessayer sans recréer le dossier. "
                        + UiErrorMessages.For(exception);
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

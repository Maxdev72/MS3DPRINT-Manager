using System.Windows;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class ClassifyFileWindow : Window, ICreatedFolderDialog
{
    private readonly ClassifyFileViewModel _viewModel;
    private readonly ProjectFileTransferService _transfer = new();
    private int _projectLoadVersion;
    private bool _closed;

    public ClassifyFileWindow(string storageRoot, string? initialProjectPath = null)
    {
        InitializeComponent();
        _viewModel = new ClassifyFileViewModel(storageRoot, initialProjectPath, loadProjects: false);
        DataContext = _viewModel;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += async (_, _) => await LoadProjectsSafelyAsync();
        Closed += (_, _) => { _closed = true; ++_projectLoadVersion; };
    }

    public string? CreatedPath { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choisir le fichier à classer", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _viewModel.SourcePath = dialog.FileName;
    }

    private async void RetryProjects_Click(object sender, RoutedEventArgs e) => await LoadProjectsSafelyAsync();

    private async Task LoadProjectsSafelyAsync()
    {
        var loadVersion = ++_projectLoadVersion;
        ProjectBox.IsEnabled = false;
        DestinationBox.IsEnabled = false;
        ProjectLoadingText.Visibility = Visibility.Visible;
        ProjectLoadErrorText.Visibility = Visibility.Collapsed;
        RetryProjectsButton.Visibility = Visibility.Collapsed;
        try
        {
            var choices = await Task.Run(_viewModel.ReadProjectChoices);
            if (_closed || loadVersion != _projectLoadVersion) return;
            _viewModel.ApplyProjectChoices(choices);
            ProjectBox.IsEnabled = true;
            DestinationBox.IsEnabled = true;
        }
        catch (Exception exception)
        {
            if (_closed || loadVersion != _projectLoadVersion) return;
            ProjectLoadErrorText.Text = UiErrorMessages.For(exception);
            ProjectLoadErrorText.Visibility = Visibility.Visible;
            RetryProjectsButton.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!_closed && loadVersion == _projectLoadVersion) ProjectLoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void Move_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!_viewModel.IsReady)
        {
            ErrorText.Text = "Choisissez un fichier, un projet et un dossier de destination.";
            return;
        }

        var answer = MessageBox.Show(this,
            $"Déplacer ce fichier ?\n\nSource : {_viewModel.SourcePath}\nDestination : {_viewModel.DestinationPath}\n\nLe fichier ne sera plus présent à son emplacement d’origine.",
            "Confirmer le déplacement", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        MoveButton.IsEnabled = false;
        try
        {
            var destination = _viewModel.CreateDestination();
            CreatedPath = await Task.Run(() => _transfer.Move(_viewModel.SourcePath, _viewModel.ProjectPath, destination));
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = UiErrorMessages.For(exception);
            MoveButton.IsEnabled = true;
        }
    }
}

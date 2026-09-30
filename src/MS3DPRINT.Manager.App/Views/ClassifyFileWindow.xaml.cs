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

    public ClassifyFileWindow(string storageRoot, string? initialProjectPath = null)
    {
        InitializeComponent();
        _viewModel = new ClassifyFileViewModel(storageRoot, initialProjectPath);
        DataContext = _viewModel;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
    }

    public string? CreatedPath { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choisir le fichier à classer", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _viewModel.SourcePath = dialog.FileName;
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

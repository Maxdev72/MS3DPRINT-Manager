using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class ProjectDetailView : UserControl
{
    private readonly ProjectDetailViewModel _viewModel;
    private int _fileLoadVersion;

    public ProjectDetailView(ProjectDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            StatusBox.SelectedIndex = (int)_viewModel.Status;
            await LoadFilesAsync(_viewModel.ReadFiles);
        };
    }

    public event EventHandler? BackRequested;
    public event EventHandler? ClassifyRequested;
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusBox.SelectedIndex >= 0) _viewModel.Status = (ProjectStatus)StatusBox.SelectedIndex;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.Save(); MessageText.Text = "Projet enregistré."; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }
    private async void Up_Click(object sender, RoutedEventArgs e) => await LoadFilesAsync(_viewModel.ReadParentFiles);
    private void Classify_Click(object sender, RoutedEventArgs e) => ClassifyRequested?.Invoke(this, EventArgs.Empty);
    private void File_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedItem is not ProjectFileEntry entry) return;
        FilesList.SelectedItem = null;
        if (entry.IsDirectory) _ = LoadFilesAsync(() => _viewModel.ReadFilesForDirectory(entry));
        else
        {
            try { ExplorerService.Open(entry.FullPath); }
            catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
        }
    }

    private async Task LoadFilesAsync(Func<ProjectFileListing> read)
    {
        var loadVersion = ++_fileLoadVersion;
        MessageText.Text = "Chargement des fichiers…";
        try
        {
            var listing = await Task.Run(read);
            if (loadVersion != _fileLoadVersion) return;
            _viewModel.ApplyFileListing(listing);
            MessageText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            if (loadVersion == _fileLoadVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
    }
}

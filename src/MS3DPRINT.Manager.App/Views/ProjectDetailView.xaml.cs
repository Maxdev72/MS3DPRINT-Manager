using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class ProjectDetailView : UserControl, IUnsavedChangesPage
{
    private readonly ProjectDetailViewModel _viewModel;
    private int _fileLoadVersion;
    private ProjectFileEntry? _selectedPreviewFile;
    private readonly Action<string?, Window?> _showModelPreview;
    private readonly FolderSizeService _folderSizes = new();

    public ProjectDetailView(ProjectDetailViewModel viewModel, Action<string?, Window?>? showModelPreview = null)
    {
        _showModelPreview = showModelPreview ?? ModelPreviewLauncher.Show;
        InitializeComponent();
        Controls.FileManagement.ConfigureFileTable(FilesList);
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
    public bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    private void Save_Click(object sender, RoutedEventArgs e) => TrySaveChanges();

    public bool TrySaveChanges()
    {
        try { _viewModel.Save(); MessageText.Text = "Projet enregistré."; return true; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); return false; }
    }
    private async void Up_Click(object sender, RoutedEventArgs e) => await LoadFilesAsync(_viewModel.ReadParentFiles);
    private void Classify_Click(object sender, RoutedEventArgs e) => ClassifyRequested?.Invoke(this, EventArgs.Empty);
    private void File_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPreviewFile = FilesList.SelectedItem is ProjectFileEntry { IsDirectory: false } entry && ThreeDFileSupport.IsPreviewable(entry.FullPath) ? entry : null;
        Preview3DButton.IsEnabled = _selectedPreviewFile is not null;
    }
    public void ShowInformation() => DetailTabs.SelectedIndex = 1;

    public void OpenEntry(ProjectFileEntry entry)
    {
        if (entry.IsDirectory)
        {
            _selectedPreviewFile = null;
            Preview3DButton.IsEnabled = false;
            _ = LoadFilesAsync(() => _viewModel.ReadFilesForDirectory(entry));
        }
        else if (ThreeDFileSupport.IsPreviewable(entry.FullPath))
        {
            _selectedPreviewFile = entry;
            Preview3DButton.IsEnabled = true;
            try { _showModelPreview(entry.FullPath, Window.GetWindow(this)); }
            catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
        }
        else if (PreviewFileSupport.GetKind(entry.FullPath) != PreviewFileKind.None)
        {
            _selectedPreviewFile = null;
            Preview3DButton.IsEnabled = false;
            try
            {
                var preview = new DocumentPreviewWindow(entry.FullPath);
                if (Window.GetWindow(this) is Window owner) preview.Owner = owner;
                preview.ShowDialog();
            }
            catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
        }
        else
        {
            _selectedPreviewFile = null;
            Preview3DButton.IsEnabled = false;
            try { ExplorerService.Open(entry.FullPath); }
            catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
        }
    }

    private void Preview3D_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreviewFile is null) return;
        _showModelPreview(_selectedPreviewFile.FullPath, Window.GetWindow(this));
    }

    public void ConfigureFileManagement(string root, Window owner)
        => Controls.FileManagement.Attach(FileActionsHost, FilesList, root,
            () => string.IsNullOrWhiteSpace(_viewModel.CurrentDirectory) ? _viewModel.ProjectPath : _viewModel.CurrentDirectory, RefreshAsync, OpenEntry, owner, _viewModel.ProjectPath);

    public Task RefreshAsync() => LoadFilesAsync(_viewModel.ReadFiles);

    private async Task LoadFilesAsync(Func<ProjectFileListing> read)
    {
        var loadVersion = ++_fileLoadVersion;
        _selectedPreviewFile = null;
        Preview3DButton.IsEnabled = false;
        MessageText.Text = "Chargement des fichiers…";
        try
        {
            var listing = await Task.Run(read);
            if (loadVersion != _fileLoadVersion) return;
            _viewModel.ApplyFileListing(listing);
            MessageText.Text = string.Empty;
            _ = LoadFolderSizesAsync(listing, loadVersion);
        }
        catch (Exception exception)
        {
            if (loadVersion == _fileLoadVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private async Task LoadFolderSizesAsync(ProjectFileListing listing, int loadVersion)
    {
        var folders = listing.Entries.Where(entry => entry.IsDirectory).ToArray();
        if (folders.Length == 0) return;
        var sizes = await Task.WhenAll(folders.Select(async folder =>
            (folder.FullPath, await _folderSizes.GetSizeAsync(folder.FullPath, CancellationToken.None))));
        if (loadVersion != _fileLoadVersion) return;
        _viewModel.ApplyFolderSizes(sizes.ToDictionary(item => item.FullPath, item => item.Item2));
    }
}

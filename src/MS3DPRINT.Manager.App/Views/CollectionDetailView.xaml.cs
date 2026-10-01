using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class CollectionDetailView : UserControl
{
    private readonly CollectionDetailViewModel _viewModel;
    private int _fileLoadVersion;
    private ProjectFileEntry? _selectedPreviewFile;

    public CollectionDetailView(CollectionDetailViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await LoadFilesAsync(_viewModel.ReadFiles);
    }

    public event EventHandler? BackRequested;

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void Up_Click(object sender, RoutedEventArgs e) => await LoadFilesAsync(_viewModel.ReadParentFiles);

    private void File_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedItem is not ProjectFileEntry entry) return;
        FilesList.SelectedItem = null;
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
            MessageText.Text = "Fichier 3D sélectionné. Utilisez « Visualiser en 3D » pour l’ouvrir.";
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
        ModelPreviewLauncher.Show(_selectedPreviewFile.FullPath, Window.GetWindow(this));
    }

    public Task RefreshAsync() => LoadFilesAsync(_viewModel.ReadFiles);

    private async Task LoadFilesAsync(Func<CollectionFileListing> read)
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
        }
        catch (Exception exception)
        {
            if (loadVersion == _fileLoadVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
    }
}

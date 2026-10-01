using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.Preview;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class ModelPreviewWindow : Window
{
    private readonly string? _initialPath;
    private int _loadVersion;

    public ModelPreviewWindow() : this(null) { }

    public ModelPreviewWindow(string? path)
    {
        _initialPath = path is null ? null : Path.GetFullPath(path);
        InitializeComponent();
        TitleText.Text = "Visualiseur 3D";
        PathText.Text = "Déposez un STL ou un OBJ ici, ou choisissez un fichier.";
        MessageText.Text = "Rotation : clic gauche · Zoom : molette · Déplacement : clic droit.";
        if (_initialPath is not null) Loaded += async (_, _) => await LoadModelAsync(_initialPath);
    }

    private async Task LoadModelAsync(string path)
    {
        var version = ++_loadVersion;
        MessageText.Text = "Chargement du modèle…";
        try
        {
            var model = await Task.Run(() => LoadModel(path));
            if (version != _loadVersion) return;
            var dimensions = ModelDimensions.FromBounds(model.Bounds);
            ModelVisual.Content = model;
            TitleText.Text = Path.GetFileName(path);
            PathText.Text = path;
            DimensionsText.Text = dimensions.ToDisplayText();
            Viewport3D.ZoomExtents();
            ResetButton.IsEnabled = true;
            MessageText.Text = "Rotation : clic gauche · Zoom : molette · Déplacement : clic droit.";
        }
        catch (Exception exception)
        {
            if (version == _loadVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private static Model3D LoadModel(string path)
    {
        var model = new ModelImporter().Load(path) ?? throw new InvalidOperationException("Le modèle 3D ne contient aucune géométrie affichable.");
        if (model.CanFreeze) model.Freeze();
        return model;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var filePicker = new OpenFileDialog
        {
            Title = "Choisir un fichier 3D",
            Filter = "Modèles 3D (*.stl;*.obj)|*.stl;*.obj|Fichiers STL (*.stl)|*.stl|Fichiers OBJ (*.obj)|*.obj",
            CheckFileExists = true,
            Multiselect = false
        };
        if (filePicker.ShowDialog(this) != true) return;
        try { await LoadModelAsync(ModelFileSelection.Select([filePicker.FileName])); }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            paths.Length == 1 && ThreeDFileSupport.IsPreviewable(paths[0])
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                throw new ArgumentException("Déposez un fichier STL ou OBJ.");
            await LoadModelAsync(ModelFileSelection.Select(paths));
        }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Viewport3D.ZoomExtents();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace MS3DPRINT.Manager.App.Views;

public partial class ModelPreviewWindow : Window
{
    private readonly string _path;

    public ModelPreviewWindow(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        InitializeComponent();
        TitleText.Text = Path.GetFileName(_path);
        PathText.Text = _path;
        Loaded += async (_, _) => await LoadModelAsync();
    }

    private async Task LoadModelAsync()
    {
        MessageText.Text = "Chargement du modèle…";
        try
        {
            var model = await Task.Run(LoadModel);
            ModelVisual.Content = model;
            Viewport3D.ZoomExtents();
            ResetButton.IsEnabled = true;
            MessageText.Text = "Rotation : clic gauche · Zoom : molette · Déplacement : clic droit.";
        }
        catch (Exception exception)
        {
            MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private Model3D LoadModel()
    {
        var model = new ModelImporter().Load(_path) ?? throw new InvalidOperationException("Le modèle 3D ne contient aucune géométrie affichable.");
        if (model.CanFreeze) model.Freeze();
        return model;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Viewport3D.ZoomExtents();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

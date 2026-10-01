using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Color4 = HelixToolkit.Maths.Color4;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.Preview;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class GpuModelPreviewWindow : Window
{
    private readonly string? _initialPath;
    private readonly DefaultEffectsManager _effectsManager;
    private readonly List<MeshGeometryModel3D> _models = [];
    private CancellationTokenSource? _activeLoad;
    private GpuPreviewScene? _scene;
    private string? _requestedPath;
    private int _loadVersion;
    private System.Windows.Media.Color _displayColor = System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF);

    public GpuModelPreviewWindow() : this(null) { }

    public GpuModelPreviewWindow(string? path)
    {
        _initialPath = path is null ? null : Path.GetFullPath(path);
        InitializeComponent();
        _effectsManager = new DefaultEffectsManager();
        Viewport3D.EffectsManager = _effectsManager;
        Viewport3D.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(3, 3, 3),
            LookDirection = new Vector3D(-3, -3, -3),
            UpDirection = new Vector3D(0, 0, 1)
        };
        TitleText.Text = "Visualiseur 3D";
        PathText.Text = "Déposez un STL ou un OBJ ici, ou choisissez un fichier.";
        MessageText.Text = "Rotation : clic gauche · Zoom : molette · Déplacement : clic droit.";
        if (_initialPath is not null) Loaded += async (_, _) => await LoadModelAsync(_initialPath);
        Closed += (_, _) =>
        {
            ++_loadVersion;
            _activeLoad?.Cancel();
            ModelsGroup.Children.Clear();
            _effectsManager.Dispose();
        };
    }

    private async Task LoadModelAsync(string path)
    {
        var version = ++_loadVersion;
        _activeLoad?.Cancel();
        using var load = new CancellationTokenSource();
        _activeLoad = load;
        _requestedPath = path;
        MessageText.Text = "Chargement du modèle…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var scene = await Task.Run(() => GpuModelLoader.Load(path, load.Token), load.Token);
            if (version != _loadVersion) return;
            ModelsGroup.Children.Clear();
            _models.Clear();
            _scene = scene;
            foreach (var mesh in scene.Parts)
            {
                var model = new MeshGeometryModel3D { Geometry = mesh };
                _models.Add(model);
                ModelsGroup.Children.Add(model);
            }
            ApplyDisplay();
            SetCamera(scene.Bounds);
            ResetButton.IsEnabled = true;
            TitleText.Text = Path.GetFileName(path);
            PathText.Text = path;
            DimensionsText.Text = ModelDimensions.FromBounds(scene.Bounds).ToDisplayText();
            MessageText.Text = $"{scene.TriangleCount:N0} triangles · chargement {stopwatch.Elapsed.TotalSeconds:N1} s · rotation : clic gauche, zoom : molette.";
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (version == _loadVersion) MessageText.Text = UiErrorMessages.For(exception) + " Utilisez « Rendu classique » si nécessaire.";
        }
        finally
        {
            if (ReferenceEquals(_activeLoad, load)) _activeLoad = null;
        }
    }

    private void SetCamera(Rect3D bounds)
    {
        var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var diagonal = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
        var radius = diagonal > 0 && double.IsFinite(diagonal) ? diagonal : 1;
        var distance = radius * 1.8;
        Viewport3D.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(center.X + distance, center.Y + distance, center.Z + distance),
            LookDirection = new Vector3D(-distance, -distance, -distance),
            UpDirection = new Vector3D(0, 0, 1),
            NearPlaneDistance = radius / 10000,
            FarPlaneDistance = radius * 100
        };
    }

    private void ApplyDisplay()
    {
        var wireframe = RenderModeBox.SelectedIndex == 1;
        foreach (var model in _models)
        {
            model.FillMode = wireframe ? SharpDX.Direct3D11.FillMode.Wireframe : SharpDX.Direct3D11.FillMode.Solid;
            model.CullMode = SharpDX.Direct3D11.CullMode.None;
            var color = new Color4(_displayColor.R / 255f, _displayColor.G / 255f, _displayColor.B / 255f, 1);
            model.Material = new PhongMaterial { DiffuseColor = color };
            model.WireframeColor = _displayColor;
        }
        if (_scene is not null)
            MessageText.Text = $"{_scene.TriangleCount:N0} triangles · mode {(wireframe ? "filaire" : "solide")}.";
    }

    private void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModelsGroup is not null) ApplyDisplay();
    }

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ColorHexBox is null) return;
        ColorHexBox.Text = ColorPresetBox.SelectedIndex switch
        {
            1 => "#B8C2CC", 2 => "#66CC99", 3 => "#FFB454",
            4 => "#FF6B6B", 5 => "#FFFFFF", _ => "#58A6FF"
        };
        TryApplyColor();
    }

    private void ApplyColor_Click(object sender, RoutedEventArgs e) => TryApplyColor();

    private void TryApplyColor()
    {
        if (!PreviewColorParser.TryParse(ColorHexBox.Text, out var color))
        {
            MessageText.Text = "Couleur invalide : utilisez #RRGGBB (ex. #58A6FF).";
            return;
        }
        _displayColor = color;
        ColorSwatch.Background = new SolidColorBrush(color);
        ApplyDisplay();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choisir un fichier 3D",
            Filter = "Modèles 3D (*.stl;*.obj)|*.stl;*.obj|Fichiers STL (*.stl)|*.stl|Fichiers OBJ (*.obj)|*.obj",
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        try { await LoadModelAsync(ModelFileSelection.Select([picker.FileName])); }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            paths.Length == 1 && ThreeDFileSupport.IsPreviewable(paths[0])
            ? DragDropEffects.Copy : DragDropEffects.None;
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

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_scene is not null) SetCamera(_scene.Bounds);
    }

    private void Classic_Click(object sender, RoutedEventArgs e)
    {
        var classic = new ModelPreviewWindow(_requestedPath ?? _initialPath) { Owner = this };
        classic.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

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
    private CancellationTokenSource? _activeAnalysis;
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
            _activeAnalysis?.Cancel();
            AnalysisGroup.Children.Clear();
            ModelsGroup.Children.Clear();
            _effectsManager.Dispose();
        };
    }

    private async Task LoadModelAsync(string path)
    {
        var version = ++_loadVersion;
        _activeAnalysis?.Cancel();
        AnalysisGroup.Children.Clear();
        AnalyzeButton.IsEnabled = false;
        ConfirmUnitsBox.IsChecked = false;
        AnalysisText.Text = "Confirmez l’échelle du nouveau modèle avant l’analyse.";
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
            AnalyzeButton.IsEnabled = _activeAnalysis is null;
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

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_scene is null || _activeAnalysis is not null) return;
        if (ConfirmUnitsBox.IsChecked != true ||
            !double.TryParse(UnitScaleBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale) ||
            !double.TryParse(ThicknessBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var threshold) ||
            !double.IsFinite(scale) || scale <= 0 || !double.IsFinite(threshold) || threshold <= 0)
        {
            AnalysisText.Text = "Saisissez une échelle et un seuil positifs, puis confirmez l’échelle (ex. 1 pour mm, 25,4 pour pouces).";
            return;
        }
        var scene = _scene;
        int version = _loadVersion;
        using var cancellation = new CancellationTokenSource();
        _activeAnalysis = cancellation;
        AnalyzeButton.IsEnabled = false;
        CancelAnalysisButton.IsEnabled = true;
        AnalysisGroup.Children.Clear();
        AnalysisText.Text = "Analyse en cours… La navigation reste disponible.";
        try
        {
            var result = await Task.Run(() =>
            {
                if (scene.TriangleCount > 2_000_000) throw new ArgumentException("Analyse limitée à 2 millions de triangles.");
                var triangles = new List<WallTriangle>(scene.TriangleCount);
                foreach (var part in scene.Parts)
                    for (int i = 0; i + 2 < part.Indices!.Count; i += 3)
                    {
                        if ((i & 1023) == 0) cancellation.Token.ThrowIfCancellationRequested();
                        triangles.Add(new(part.Positions![part.Indices[i]], part.Positions[part.Indices[i+1]], part.Positions[part.Indices[i+2]]));
                    }
                return WallThicknessAnalyzer.Analyze(triangles, scale, threshold, cancellation.Token);
            }, cancellation.Token);
            if (version != _loadVersion || cancellation.IsCancellationRequested) return;
            var selected = result.ThinSamples.Select(s => s.TriangleIndex).ToHashSet();
            int offset = 0;
            foreach (var part in scene.Parts)
            {
                var indices = new HelixToolkit.IntCollection();
                var positions = new HelixToolkit.Vector3Collection();
                foreach (int triangle in selected.Where(i => i >= offset && i < offset + part.Indices!.Count / 3))
                {
                    int i = (triangle - offset) * 3;
                    for (int j = 0; j < 3; j++)
                    {
                        indices.Add(positions.Count);
                        positions.Add(part.Positions![part.Indices![i+j]]);
                    }
                }
                offset += part.Indices!.Count / 3;
                if (indices.Count == 0) continue;
                AnalysisGroup.Children.Add(new MeshGeometryModel3D
                {
                    Geometry = new HelixToolkit.SharpDX.MeshGeometry3D { Positions = positions, Indices = indices },
                    Material = new PhongMaterial { DiffuseColor = new Color4(1, 0.35f, 0, 1) },
                    FillMode = SharpDX.Direct3D11.FillMode.Wireframe,
                    CullMode = SharpDX.Direct3D11.CullMode.None,
                    DepthBias = -100
                });
            }
            AnalysisText.Text = $"Seuil {threshold:G} mm · échelle {scale:G} mm/unité : {result.ThinSamples.Count} zones potentiellement fines (orange), {result.SampleCount:N0}/{result.TriangleCount:N0} centres de faces sondés. " +
                $"Arêtes ouvertes : {result.BoundaryEdges:N0} ; non-manifold : {result.NonManifoldEdges:N0} ; orientations incohérentes : {result.InconsistentEdges:N0} ; triangles invalides : {result.InvalidTriangles:N0}. " +
                (result.BudgetExhausted ? "Budget atteint : analyse partielle. " : "") +
                "Sondage non exhaustif selon les normales : détails fins et parois obliques peuvent être manqués. Soudures exactes ; auto-intersections et défauts aux sommets non vérifiés. Un maillage ouvert/incohérent rend les distances ambiguës. L’absence d’alerte ne garantit pas l’imprimabilité ; vérifier dans le trancheur.";
        }
        catch (OperationCanceledException) { if (version == _loadVersion) AnalysisText.Text = "Analyse annulée."; }
        catch (Exception exception) { if (version == _loadVersion) AnalysisText.Text = "Analyse indisponible : " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_activeAnalysis, cancellation)) _activeAnalysis = null;
            CancelAnalysisButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = _scene is not null && _activeLoad is null;
        }
    }

    private void CancelAnalysis_Click(object sender, RoutedEventArgs e) => _activeAnalysis?.Cancel();

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

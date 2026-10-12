using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Native 3D viewer for STL and OBJ files, with a thin-wall advisory analysis.</summary>
public sealed class ModelViewerWindow : Window
{
    private readonly MeshViewport _viewport = new();
    private readonly TextBlock _title = new() { Text = "Visualiseur 3D", FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _location = new() { Text = "Déposez un STL ou un OBJ ici, ou choisissez un fichier.", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _dimensions = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { Text = "Rotation : clic gauche · Zoom : molette · Déplacement : clic droit.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _analysis = new() { Text = "Analyse indicative sur échantillons, sans certification d’imprimabilité. Aucun fichier modifié.", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Solide", "Filaire" }, SelectedIndex = 0, Width = 120 };
    private readonly ComboBox _preset = new() { ItemsSource = new[] { "Bleu", "Gris", "Vert", "Orange", "Rouge", "Blanc" }, SelectedIndex = 0, Width = 120 };
    private readonly TextBox _hex = new() { Text = "#58A6FF", Width = 110 };
    private readonly Border _swatch = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#58A6FF")) };
    private readonly TextBox _threshold = new() { Text = "1", Width = 70 };
    private readonly TextBox _scale = new() { Text = "1", Width = 70 };
    private readonly CheckBox _confirmUnits = new() { Content = "Je confirme cette échelle" };
    private readonly Button _analyze = new() { Content = "Analyser les parois", IsEnabled = false };
    private readonly Button _cancelAnalysis = new() { Content = "Annuler", IsEnabled = false };
    private readonly Button _reset = new() { Content = "Recentrer", IsEnabled = false };
    private CancellationTokenSource? _activeLoad;
    private CancellationTokenSource? _activeAnalysis;
    private MeshModel? _mesh;
    private int _loadVersion;

    public ModelViewerWindow(string? path = null)
    {
        Title = "Visualiseur 3D";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 1040; Height = 780; MinWidth = 700; MinHeight = 520;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        var muted = (IBrush?)(Application.Current?.TryFindResource("MutedTextBrush", Application.Current.ActualThemeVariant, out var value) == true ? value : null) ?? Brushes.Gray;
        _location.Foreground = muted;
        _message.Foreground = muted;

        var browse = new Button { Content = "Choisir un fichier…", VerticalAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) => await BrowseAsync();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        header.Children.Add(new StackPanel { Spacing = 2, Children = { _title, _location } });
        Grid.SetColumn(browse, 1);
        header.Children.Add(browse);

        _mode.SelectionChanged += (_, _) => { _viewport.Wireframe = _mode.SelectedIndex == 1; UpdateModeMessage(); _viewport.RequestRender(false); };
        _preset.SelectionChanged += (_, _) =>
        {
            _hex.Text = _preset.SelectedIndex switch { 1 => "#B8C2CC", 2 => "#66CC99", 3 => "#FFB454", 4 => "#FF6B6B", 5 => "#FFFFFF", _ => "#58A6FF" };
            ApplyColor();
        };
        var apply = new Button { Content = "Appliquer" };
        apply.Click += (_, _) => ApplyColor();
        var display = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Label("Affichage"), _mode, Label("Couleur"), _preset, _hex, _swatch, apply }
        };

        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _cancelAnalysis.Click += (_, _) => _activeAnalysis?.Cancel();
        var analysisRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Label("Seuil paroi"), _threshold, Label("mm"), Label("Échelle"), _scale, Label("mm par unité du modèle"), _confirmUnits, _analyze, _cancelAnalysis }
        };
        var hint = new TextBlock { Text = "STL/OBJ : unités inconnues. Choisissez le seuil selon votre buse, matériau et réglages du trancheur.", Foreground = muted, TextWrapping = TextWrapping.Wrap };
        var analysisPanel = new StackPanel { Spacing = 6, Children = { analysisRow, hint, _analysis } };

        _reset.Click += (_, _) => _viewport.ResetView();
        var close = new Button { Content = "Fermer" };
        close.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(new StackPanel { Spacing = 2, Children = { _dimensions, _message } });
        Grid.SetColumn(_reset, 1); footer.Children.Add(_reset);
        Grid.SetColumn(close, 2); footer.Children.Add(close);

        var viewer = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _viewport };
        var root = new Grid { Margin = new Thickness(18), RowSpacing = 12, RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        root.Children.Add(header);
        Grid.SetRow(display, 1); root.Children.Add(display);
        Grid.SetRow(analysisPanel, 2); root.Children.Add(analysisPanel);
        Grid.SetRow(viewer, 3); root.Children.Add(viewer);
        Grid.SetRow(footer, 4); root.Children.Add(footer);
        Content = root;

        if (path is not null) Opened += async (_, _) => await LoadModelAsync(Path.GetFullPath(path));
        Closed += (_, _) => { _loadVersion++; _activeLoad?.Cancel(); _activeAnalysis?.Cancel(); };
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier 3D",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Modèles 3D (STL, OBJ)") { Patterns = ["*.stl", "*.obj"] }]
        });
        var local = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(local)) return;
        try { await LoadModelAsync(ModelFileSelection.Select([local])); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FileNotFoundException)
        { _message.Text = exception.Message; }
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        e.DragEffects = path is not null && ThreeDFileSupport.IsPreviewable(path) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            await LoadModelAsync(ModelFileSelection.Select(paths));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FileNotFoundException)
        { _message.Text = exception.Message; }
    }

    private async Task LoadModelAsync(string path)
    {
        var version = ++_loadVersion;
        _activeAnalysis?.Cancel();
        _activeLoad?.Cancel();
        _analyze.IsEnabled = false;
        _confirmUnits.IsChecked = false;
        _analysis.Text = "Confirmez l’échelle du nouveau modèle avant l’analyse.";
        using var load = new CancellationTokenSource();
        _activeLoad = load;
        _message.Text = "Chargement du modèle…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var mesh = await Task.Run(() => MeshLoader.Load(path, load.Token), load.Token);
            if (version != _loadVersion) return;
            _mesh = mesh;
            _viewport.SetMesh(mesh);
            _analyze.IsEnabled = _activeAnalysis is null;
            _reset.IsEnabled = true;
            _title.Text = Path.GetFileName(path);
            _location.Text = path;
            _dimensions.Text = ModelDimensions.FromMesh(mesh).ToDisplayText();
            _message.Text = $"{mesh.TriangleCount:N0} triangles · chargement {stopwatch.Elapsed.TotalSeconds:N1} s · rotation : clic gauche, zoom : molette.";
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or FormatException or UnauthorizedAccessException)
        {
            if (version == _loadVersion) _message.Text = "Impossible d’ouvrir ce modèle : " + exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_activeLoad, load)) _activeLoad = null;
        }
    }

    private void UpdateModeMessage()
    {
        if (_mesh is not null)
            _message.Text = $"{_mesh.TriangleCount:N0} triangles · mode {(_viewport.Wireframe ? "filaire" : "solide")}.";
    }

    private void ApplyColor()
    {
        if (!PreviewColorParser.TryParse(_hex.Text, out var color))
        {
            _message.Text = "Couleur invalide : utilisez #RRGGBB (ex. #58A6FF).";
            return;
        }
        _viewport.ModelColor = color;
        _swatch.Background = new SolidColorBrush(color);
        _viewport.RequestRender(false);
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0;

    private async Task AnalyzeAsync()
    {
        var mesh = _mesh;
        if (mesh is null || _activeAnalysis is not null) return;
        if (_confirmUnits.IsChecked != true || !TryNumber(_scale.Text, out var scale) || !TryNumber(_threshold.Text, out var threshold))
        {
            _analysis.Text = "Saisissez une échelle et un seuil positifs, puis confirmez l’échelle (ex. 1 pour mm, 25,4 pour pouces).";
            return;
        }
        var version = _loadVersion;
        using var cancellation = new CancellationTokenSource();
        _activeAnalysis = cancellation;
        _analyze.IsEnabled = false;
        _cancelAnalysis.IsEnabled = true;
        _viewport.Markers = null;
        _viewport.RequestRender(false);
        _analysis.Text = "Analyse en cours… La navigation reste disponible.";
        try
        {
            var result = await Task.Run(() =>
            {
                if (mesh.TriangleCount > 2_000_000) throw new ArgumentException("Analyse limitée à 2 millions de triangles.");
                var triangles = new List<WallTriangle>(mesh.TriangleCount);
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    if ((i & 1023) == 0) cancellation.Token.ThrowIfCancellationRequested();
                    triangles.Add(new(mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]]));
                }
                return WallThicknessAnalyzer.Analyze(triangles, scale, threshold, cancellation.Token);
            }, cancellation.Token);
            if (version != _loadVersion || cancellation.IsCancellationRequested) return;
            _viewport.Markers = result.ThinSamples.Select(sample => sample.Position).ToArray();
            _viewport.RequestRender(false);
            var topologyBlocksAnalysis = result.BoundaryEdges != 0 || result.NonManifoldEdges != 0 || result.InconsistentEdges != 0 || result.InvalidTriangles != 0;
            _analysis.Text = $"Seuil {threshold:G} mm · échelle {scale:G} mm/unité : {result.ThinSamples.Count} zones potentiellement fines (points orange), {result.SampleCount:N0}/{result.TriangleCount:N0} centres de faces sondés. " +
                $"Arêtes ouvertes : {result.BoundaryEdges:N0} ; non-manifold : {result.NonManifoldEdges:N0} ; orientations incohérentes : {result.InconsistentEdges:N0} ; triangles invalides : {result.InvalidTriangles:N0}. " +
                (topologyBlocksAnalysis ? "Mesure de paroi non effectuée : corrigez d’abord le maillage pour distinguer matière et vide. " : "") +
                (result.BudgetExhausted ? "Budget atteint : analyse partielle. " : "") +
                "Sondage non exhaustif selon les normales : détails fins et parois obliques peuvent être manqués. Soudures exactes ; auto-intersections et défauts aux sommets non vérifiés. L’absence d’alerte ne garantit pas l’imprimabilité ; vérifier dans le trancheur.";
        }
        catch (OperationCanceledException) { if (version == _loadVersion) _analysis.Text = "Analyse annulée."; }
        catch (ArgumentException exception) { if (version == _loadVersion) _analysis.Text = "Analyse indisponible : " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_activeAnalysis, cancellation)) _activeAnalysis = null;
            _cancelAnalysis.IsEnabled = false;
            _analyze.IsEnabled = _mesh is not null && _activeLoad is null;
        }
    }
}

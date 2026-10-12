using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Native 3D viewer for STL and OBJ files, shown inside the main window, with a thin-wall advisory analysis.</summary>
public sealed class ModelViewerView : UserControl
{
    private static readonly (string Name, string Hex)[] Palette =
    [
        ("Bleu", "#58A6FF"), ("Gris", "#B8C2CC"), ("Vert", "#66CC99"), ("Orange", "#FFB454"), ("Rouge", "#FF6B6B"), ("Blanc", "#FFFFFF")
    ];

    private IMeshViewport _viewport = new MeshViewport();
    private readonly Grid _stage = new();
    private AxesGizmo? _gizmo;
    private readonly Func<IReadOnlyList<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>>? _loadFilaments;
    private readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _infill = new() { Text = "15" };
    private readonly TextBox _walls = new() { Text = "1,2" };
    private readonly TextBlock _volumeResult = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _volumeNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private MeshMeasures? _measures;
    private readonly MeasureOverlay _overlay = new();
    private readonly ToggleButton _measureToggle = new() { Content = "Mesurer des distances", IsEnabled = false };
    private readonly TextBlock _measureList = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _clearMeasures = new() { Content = "Effacer les mesures", IsEnabled = false };
    private readonly List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B)> _segments = [];
    private System.Numerics.Vector3? _pendingPoint;
    private readonly ComboBox _renderer = new() { ItemsSource = new[] { "Accéléré (carte graphique)", "Logiciel (processeur)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _rendererNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private DispatcherTimer? _gpuWatch;
    private readonly TextBlock _title = new() { Text = "Aucun modèle", FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _location = new() { Classes = { "caption" }, TextTrimming = TextTrimming.PrefixCharacterEllipsis, IsVisible = false };
    private readonly TextBlock _dimensions = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _message = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _analysis = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = "Analyse indicative sur échantillons, sans certification d’imprimabilité. Aucun fichier n’est modifié." };
    private readonly TextBlock _analysisSummary = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Solide", "Filaire" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _hex = new() { Text = Palette[0].Hex };
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBox _threshold = new() { Text = "1" };
    private readonly TextBox _scale = new() { Text = "1" };
    private readonly CheckBox _confirmUnits = new() { Content = "Je confirme cette échelle" };
    private readonly Button _analyze = new() { Content = "Analyser les parois", IsEnabled = false };
    private readonly Button _cancelAnalysis = new() { Content = "Annuler", IsEnabled = false };
    private readonly Button _reset = new() { Content = "Recentrer", IsEnabled = false };
    private readonly StackPanel _emptyState = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _activeLoad;
    private CancellationTokenSource? _activeAnalysis;
    private MeshModel? _mesh;
    private int _loadVersion;

    public ModelViewerView(Action close, Func<IReadOnlyList<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>>? loadFilaments = null)
    {
        _loadFilaments = loadFilaments;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, View_DragOver);
        AddHandler(DragDrop.DropEvent, View_Drop);

        // viewport with its empty state and a navigation hint
        var hint = new TextBlock { Text = "Clic gauche : tourner · Clic droit : déplacer · Molette : zoom · R : recentrer · W : filaire", Classes = { "caption" }, Margin = new Thickness(14, 0, 14, 10), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        _emptyState.Children.Add(new TextBlock { Text = "Déposez un fichier STL ou OBJ ici", FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        _emptyState.Children.Add(new TextBlock { Text = "ou choisissez-en un sur votre ordinateur", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center });
        var emptyBrowse = new Button { Content = Icons.Label("IconOpen", "Ouvrir un fichier"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        emptyBrowse.Click += async (_, _) => await BrowseAsync();
        _emptyState.Children.Add(emptyBrowse);
        var stage = new Border
        {
            CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"),
            Child = _stage
        };
        _stage.Children.Add(_emptyState);
        _stage.Children.Add(hint);
        UseViewport(CreateGpuViewport() ?? new MeshViewport());
        _renderer.SelectionChanged += (_, _) => UseViewport(_renderer.SelectedIndex == 0 ? CreateGpuViewport() ?? new MeshViewport() : new MeshViewport());

        // side panel
        var back = new Button { Content = Icons.Label("IconArrowLeft", "Retour"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => close();
        var browse = new Button { Content = Icons.Label("IconOpen", "Ouvrir un fichier"), Classes = { "primary-action" } };
        browse.Click += async (_, _) => await BrowseAsync();
        _reset.Click += (_, _) => _viewport.ResetView();
        var fileButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        fileButtons.Children.Add(browse);
        Grid.SetColumn(_reset, 1);
        fileButtons.Children.Add(_reset);

        _mode.SelectionChanged += (_, _) => { _viewport.Wireframe = _mode.SelectedIndex == 1; UpdateModeMessage(); _viewport.RequestRender(false); };
        foreach (var (name, hex) in Palette)
        {
            var color = Color.Parse(hex);
            var swatch = new Button
            {
                Tag = hex, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(4), CornerRadius = new CornerRadius(16),
                Background = Brushes.Transparent, BorderThickness = new Thickness(2),
                Content = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(color) }
            };
            ToolTip.SetTip(swatch, name);
            AutomationProperties.SetName(swatch, "Couleur " + name.ToLowerInvariant());
            swatch.Click += (_, _) => { _hex.Text = hex; ApplyColor(); };
            _swatches.Children.Add(swatch);
        }
        _measureToggle.IsCheckedChanged += (_, _) => UpdateMeasureMode();
        _clearMeasures.Click += (_, _) => ClearMeasures();
        _scale.TextChanged += (_, _) => RefreshMeasures();
        _material.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MaterialChoice>((choice, _) => new TextBlock { Text = choice?.Label ?? string.Empty });
        _material.ItemsSource = MeshVolume.Presets.ToList();
        _material.SelectedIndex = 0;
        _material.SelectionChanged += (_, _) => RefreshVolume();
        _infill.TextChanged += (_, _) => RefreshVolume();
        _walls.TextChanged += (_, _) => RefreshVolume();
        _scale.TextChanged += (_, _) => RefreshVolume();
        RefreshVolume();
        _hex.LostFocus += (_, _) => ApplyColor();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyColor(); e.Handled = true; } };
        UpdateSwatches();

        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _cancelAnalysis.Click += (_, _) => _activeAnalysis?.Cancel();
        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 8, RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 4 };
        void Cell(Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); limits.Children.Add(control); }
        Cell(Muted("Seuil de paroi"), 0, 0);
        Cell(Muted("Échelle"), 0, 2);
        Cell(_threshold, 1, 0);
        Cell(Unit("mm"), 1, 1);
        Cell(_scale, 1, 2);
        Cell(Unit("mm / unité"), 1, 3);
        var analysisButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        analysisButtons.Children.Add(_analyze);
        Grid.SetColumn(_cancelAnalysis, 1);
        analysisButtons.Children.Add(_cancelAnalysis);
        var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        hexRow.Children.Add(Muted("Hex"));
        Grid.SetColumn(_hex, 1);
        hexRow.Children.Add(_hex);

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                back,
                _title, _location, _dimensions, _message, fileButtons,
                FormKit.SectionLabel("Affichage"), _mode, _swatches, hexRow, Muted("Rendu"), _renderer, _rendererNote,
                FormKit.SectionLabel("Mesures"), _measureToggle,
                new TextBlock { Text = "Cliquez deux points du modèle : ils s’accrochent aux sommets proches. Les longueurs utilisent l’échelle indiquée plus bas. M active, Échap annule le point en cours.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                _measureList, _clearMeasures,
                FormKit.SectionLabel("Volume et poids"), _volumeResult, Muted("Matière"), _material,
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 8, Children = { _infill, Place(Unit("% remplissage"), 1), Place(_walls, 2), Place(Unit("mm parois"), 3) } },
                _volumeNote,
                FormKit.SectionLabel("Analyse des parois"),
                new TextBlock { Text = "STL et OBJ n’indiquent pas leurs unités : choisissez le seuil selon votre buse, votre matériau et votre trancheur.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                limits, _confirmUnits, analysisButtons, _analysisSummary, _analysis
            }
        };
        var panel = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(0, 0, 10, 0), Child = side }
        };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        root.Children.Add(stage);
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Content = root;
    }

    /// <summary>Raised with the file name once a model is displayed.</summary>
    public event Action<string>? ModelLoaded;

    /// <summary>Stops running loads and analyses when the viewer is left.</summary>
    public void Release()
    {
        _loadVersion++;
        _activeLoad?.Cancel();
        _activeAnalysis?.Cancel();
    }

    private static Control Place(Control control, int column) { Grid.SetColumn(control, column); return control; }

    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Unit(string text) => new() { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };

    private IMeshViewport? CreateGpuViewport()
    {
        try
        {
            var gpu = new GpuViewportHost();
            gpu.Failed += message => Dispatcher.UIThread.Post(() => FallBackToSoftware(message));
            return gpu;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Swaps the 3D view (graphics card or processor), keeping the model, camera, colour and display mode.</summary>
    private void UseViewport(IMeshViewport next)
    {
        var previous = _viewport;
        var hadPrevious = _stage.Children.Contains(previous.Visual);
        var camera = previous.Camera;
        if (hadPrevious) _stage.Children.Remove(previous.Visual);
        if (_gizmo is not null) { _stage.Children.Remove(_gizmo); _gizmo = null; }
        _gpuWatch?.Stop();
        _viewport = next;
        _stage.Children.Insert(0, next.Visual);
        next.Wireframe = _mode.SelectedIndex == 1;
        if (PreviewColorParser.TryParse(_hex.Text, out var color)) next.ModelColor = color;
        AutomationProperties.SetName(next.Visual, "Vue 3D du modèle : souris pour tourner, déplacer et zoomer, touche R pour recentrer, W pour le mode filaire");
        if (_stage.Children.Contains(_overlay)) _stage.Children.Remove(_overlay);
        _overlay.Camera = next.Camera;
        _overlay.Mesh = _mesh;
        _stage.Children.Insert(1, _overlay);
        next.CameraChanged += () => _overlay.InvalidateVisual();
        next.Clicked += point => _ = MeasureClickAsync(point);
        next.Visual.Cursor = _measureToggle.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
        next.Visual.KeyDown += Viewport_KeyDown;
        next.Visual.PointerPressed += (_, _) => next.Visual.Focus();
        if (_mesh is not null)
        {
            next.SetMesh(_mesh);
            next.Camera.Yaw = camera.Yaw; next.Camera.Pitch = camera.Pitch; next.Camera.Zoom = camera.Zoom; next.Camera.PanX = camera.PanX; next.Camera.PanY = camera.PanY;
        }
        if (next is GpuViewportHost gpu)
        {
            _gizmo = new AxesGizmo(gpu.Camera) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            _stage.Children.Add(_gizmo);
            gpu.CameraChanged += () => _gizmo?.InvalidateVisual();
            _rendererNote.Text = "Rendu par la carte graphique : fluide même sur de gros modèles, bords lissés.";
            // No graphics context after a few seconds (remote desktop, old driver): use the processor instead.
            _gpuWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _gpuWatch.Tick += (_, _) =>
            {
                _gpuWatch?.Stop();
                if (ReferenceEquals(_viewport, gpu) && !gpu.IsReady && gpu.IsEffectivelyVisible) FallBackToSoftware("Aucun contexte graphique disponible.");
            };
            _gpuWatch.Start();
        }
        else _rendererNote.Text = "Rendu par le processeur : fonctionne partout, plus lent sur les gros modèles.";
        next.RequestRender(false);
    }

    private void FallBackToSoftware(string reason)
    {
        if (_viewport is not GpuViewportHost) return;
        UseViewport(new MeshViewport());
        _renderer.SelectedIndex = 1;
        _rendererNote.Text = "Rendu par le processeur (carte graphique indisponible : " + reason + ")";
    }

    private void Viewport_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.R) { _viewport.ResetView(); e.Handled = true; }
        else if (e.Key == Key.M && _measureToggle.IsEnabled) { _measureToggle.IsChecked = _measureToggle.IsChecked != true; e.Handled = true; }
        else if (e.Key == Key.Escape && _pendingPoint is not null) { _pendingPoint = null; RefreshMeasures(); e.Handled = true; }
        else if (e.Key == Key.W) { _mode.SelectedIndex = _mode.SelectedIndex == 1 ? 0 : 1; e.Handled = true; }
    }

    private async Task BrowseAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
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

    private void View_DragOver(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        e.DragEffects = path is not null && ThreeDFileSupport.IsPreviewable(path) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void View_Drop(object? sender, DragEventArgs e)
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

    public async Task LoadModelAsync(string path)
    {
        path = Path.GetFullPath(path);
        var version = ++_loadVersion;
        _activeAnalysis?.Cancel();
        _activeLoad?.Cancel();
        _analyze.IsEnabled = false;
        _confirmUnits.IsChecked = false;
        _analysisSummary.IsVisible = false;
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
            _overlay.Mesh = mesh;
            _measureToggle.IsEnabled = true;
            ClearMeasures();
            _ = ComputeMeasuresAsync(mesh, version);
            _viewport.SetMesh(mesh);
            _emptyState.IsVisible = false;
            _analyze.IsEnabled = _activeAnalysis is null;
            _reset.IsEnabled = true;
            _title.Text = Path.GetFileName(path);
            _location.Text = path;
            ToolTip.SetTip(_location, path);
            _location.IsVisible = _dimensions.IsVisible = true;
            _dimensions.Text = ModelDimensions.FromMesh(mesh).ToDisplayText();
            _message.Text = $"{mesh.TriangleCount:N0} triangles · chargé en {stopwatch.Elapsed.TotalSeconds:N1} s.";
            ModelLoaded?.Invoke(Path.GetFileName(path));
            _viewport.Visual.Focus();
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

    /// <summary>Loads the catalogue filaments as material choices (their density comes from the material name).</summary>
    public async Task ReloadFilamentsAsync()
    {
        if (_loadFilaments is null) return;
        IReadOnlyList<MS3DPRINT.Manager.Core.Filaments.FilamentProfile> filaments;
        try { filaments = await Task.Run(_loadFilaments); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { return; }
        var selected = (_material.SelectedItem as MaterialChoice)?.Label;
        var choices = new List<MaterialChoice>(MeshVolume.Presets);
        choices.AddRange(filaments.Select(f => new MaterialChoice($"{f.Brand} {f.Name} ({f.Material}, {f.PricePerKg:0.##} €/kg)", MeshVolume.DensityFor(f.Material), f.PricePerKg)));
        _material.ItemsSource = choices;
        _material.SelectedItem = choices.FirstOrDefault(c => c.Label == selected) ?? choices[0];
    }

    private async Task ComputeMeasuresAsync(MeshModel mesh, int version)
    {
        _measures = null;
        RefreshVolume();
        try
        {
            var measures = await Task.Run(() => MeshVolume.Compute(mesh));
            if (version != _loadVersion) return;
            _measures = measures;
        }
        catch (OperationCanceledException) { return; }
        RefreshVolume();
    }

    private void RefreshVolume()
    {
        if (_mesh is null)
        {
            _volumeResult.Text = "Ouvrez un modèle pour estimer son volume et son poids.";
            _volumeNote.Text = string.Empty;
            return;
        }
        if (_measures is not { } measures)
        {
            _volumeResult.Text = "Calcul du volume…";
            return;
        }
        var scale = TryNumber(_scale.Text, out var s) ? s : 1;
        var volumeCm3 = measures.Volume * scale * scale * scale / 1000.0;
        var areaCm2 = measures.SurfaceArea * scale * scale / 100.0;
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var lines = new List<string> { $"Volume : {volumeCm3.ToString("0.##", french)} cm³", $"Surface : {areaCm2.ToString("0.##", french)} cm²" };
        if (_material.SelectedItem is MaterialChoice material
            && double.TryParse(_infill.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var infill)
            && double.TryParse(_walls.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var walls) && infill >= 0 && walls >= 0)
        {
            var mass = MeshVolume.EstimateMass(volumeCm3, areaCm2, material.Density, infill, walls);
            var solid = volumeCm3 * material.Density;
            lines.Add($"Poids estimé : {mass.ToString("0.#", french)} g ({infill:0.##} % de remplissage)");
            lines.Add($"Poids plein : {solid.ToString("0.#", french)} g");
            if (material.PricePerKg is { } price)
                lines.Add($"Matière : {(decimal)mass * price / 1000m:0.00} €");
        }
        else lines.Add("Saisissez un remplissage et une épaisseur de parois valides pour estimer le poids.");
        _volumeResult.Text = string.Join(Environment.NewLine, lines);
        _volumeNote.Text = (measures.IsClosed switch
        {
            false => "Maillage ouvert : le volume n’est qu’approximatif, corrigez le maillage avant de vous y fier. ",
            null => "Fermeture du maillage non vérifiée (modèle très volumineux). ",
            _ => string.Empty
        }) + "Estimation indicative : le trancheur (supports, jupes, remplissage réel) donnera le poids exact. L’échelle est celle de la section Analyse des parois.";
    }

    private void UpdateMeasureMode()
    {
        var on = _measureToggle.IsChecked == true;
        _viewport.Visual.Cursor = on ? new Cursor(StandardCursorType.Cross) : null;
        if (!on) _pendingPoint = null;
        _message.Text = on ? "Mesure : cliquez le premier point sur le modèle." : _message.Text;
        RefreshMeasures();
    }

    private void ClearMeasures()
    {
        _segments.Clear();
        _pendingPoint = null;
        RefreshMeasures();
    }

    private double MeasureScale => TryNumber(_scale.Text, out var value) ? value : 1;

    private void RefreshMeasures()
    {
        _overlay.Segments = _segments.ToArray();
        _overlay.Pending = _pendingPoint;
        _overlay.Scale = MeasureScale;
        _overlay.InvalidateVisual();
        _clearMeasures.IsEnabled = _segments.Count > 0 || _pendingPoint is not null;
        var french = CultureInfo.GetCultureInfo("fr-FR");
        _measureList.IsVisible = _segments.Count > 0;
        _measureList.Text = string.Join(Environment.NewLine, _segments.Select((segment, index) =>
        {
            var delta = segment.B - segment.A;
            string Part(float value) => (Math.Abs(value) * MeasureScale).ToString("0.##", french);
            return $"{index + 1} · {MeasureOverlay.FormatLength(System.Numerics.Vector3.Distance(segment.A, segment.B), MeasureScale)}   ΔX {Part(delta.X)} · ΔY {Part(delta.Y)} · ΔZ {Part(delta.Z)}";
        }));
    }

    /// <summary>Picks the point under a click (snapping to a nearby vertex) and adds it to the current measure.</summary>
    private async Task MeasureClickAsync(Point click)
    {
        if (_measureToggle.IsChecked != true || _mesh is not { } mesh) return;
        var bounds = _viewport.Visual.Bounds;
        var camera = new ViewCamera { Yaw = _viewport.Camera.Yaw, Pitch = _viewport.Camera.Pitch, Zoom = _viewport.Camera.Zoom, PanX = _viewport.Camera.PanX, PanY = _viewport.Camera.PanY };
        var point = await Task.Run(() =>
        {
            var (origin, direction) = ViewMath.Ray(mesh, camera, bounds.Width, bounds.Height, click.X, click.Y);
            if (!MeshPicker.TryPick(mesh, origin, direction, out var hit, out var triangle)) return (System.Numerics.Vector3?)null;
            // snap to the closest corner of the triangle when it is within a few pixels of the click
            var best = hit;
            var bestDistance = 14.0;
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = mesh.Positions[mesh.Indices[triangle * 3 + corner]];
                if (!ViewMath.TryProject(mesh, camera, bounds.Width, bounds.Height, vertex, out var x, out var y)) continue;
                var distance = Math.Sqrt((x - click.X) * (x - click.X) + (y - click.Y) * (y - click.Y));
                if (distance < bestDistance) { bestDistance = distance; best = vertex; }
            }
            return best;
        });
        if (_measureToggle.IsChecked != true || !ReferenceEquals(_mesh, mesh)) return;
        if (point is not { } picked)
        {
            _message.Text = "Aucun point du modèle à cet endroit.";
            return;
        }
        if (_pendingPoint is { } start)
        {
            _segments.Add((start, picked));
            _pendingPoint = null;
            _message.Text = "Distance : " + MeasureOverlay.FormatLength(System.Numerics.Vector3.Distance(start, picked), MeasureScale) + ". Cliquez un nouveau point pour continuer.";
        }
        else
        {
            _pendingPoint = picked;
            _message.Text = "Cliquez le second point.";
        }
        RefreshMeasures();
    }

    private void UpdateModeMessage()
    {
        if (_mesh is not null)
            _message.Text = $"{_mesh.TriangleCount:N0} triangles · mode {(_viewport.Wireframe ? "filaire" : "solide")}.";
    }

    private void UpdateSwatches()
    {
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        foreach (var swatch in _swatches.Children.OfType<Button>())
        {
            var selected = string.Equals(swatch.Tag as string, _hex.Text?.Trim(), StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = selected ? accent : Brushes.Transparent;
        }
    }

    private void ApplyColor()
    {
        if (!PreviewColorParser.TryParse(_hex.Text, out var color))
        {
            _message.Text = "Couleur invalide : utilisez #RRGGBB (ex. #58A6FF).";
            return;
        }
        _viewport.ModelColor = color;
        UpdateSwatches();
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
        _analysisSummary.IsVisible = false;
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
            _analysisSummary.Text = topologyBlocksAnalysis ? "Maillage à corriger avant mesure des parois"
                : result.ThinSamples.Count == 0 ? "Aucune zone fine détectée"
                : $"{result.ThinSamples.Count} zones potentiellement fines (points orange)";
            _analysisSummary.IsVisible = true;
            _analysis.Text = $"Seuil {threshold:G} mm, échelle {scale:G} mm/unité, {result.SampleCount:N0} faces sondées sur {result.TriangleCount:N0}. " +
                $"Arêtes ouvertes : {result.BoundaryEdges:N0} ; non-manifold : {result.NonManifoldEdges:N0} ; orientations incohérentes : {result.InconsistentEdges:N0} ; triangles invalides : {result.InvalidTriangles:N0}. " +
                (topologyBlocksAnalysis ? "La mesure de paroi n’a pas été faite : corrigez d’abord le maillage. " : "") +
                (result.BudgetExhausted ? "Budget atteint : analyse partielle. " : "") +
                "Sondage non exhaustif : détails fins et parois obliques peuvent être manqués, auto-intersections non vérifiées. L’absence d’alerte ne garantit pas l’imprimabilité ; vérifiez dans le trancheur.";
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

using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>A filament colour offered on the render page: its name, its #RRGGBB code and the surface it gives.</summary>
public sealed record RenderColor(string Label, string Hex, string Finish)
{
    /// <summary>Identifies the colour with its finish, for the list of removed colours.</summary>
    public string Key => Hex.ToUpperInvariant() + "|" + Finish;
}

/// <summary>
/// Product render page, organised in tabs like KeyShot (Pièce, Matière, Ambiance, Caméra). The camera moves with the mouse in
/// the image and Blender's real-time engine (EEVEE) renders that exact view continuously; Blender stays open between renders.
/// The HD render (Cycles on the graphics card) is asked for explicitly. Choices, colours, and each part's orientation and
/// cameras are kept between visits.
/// </summary>
public sealed partial class RenderStudioView : UserControl
{
    private static readonly (string Label, string Key)[] Finishes = [("Mat", "matte"), ("Satiné / soie", "silk"), ("Brillant", "glossy"), ("Phosphorescent", "glow"), ("Chargé carbone", "carbon"), ("Chargé fibre de verre", "glassfiber")];
    private static readonly (string Label, double Height)[] Layers = [("Lisse, sans lignes", 0), ("0,12 mm", 0.12), ("0,16 mm", 0.16), ("0,20 mm", 0.2), ("0,28 mm", 0.28)];
    private static readonly (string Label, string Key)[] Scenes = [("Studio clair", "light"), ("Studio sombre", "dark"), ("Fond de couleur", "color"), ("Fond transparent (PNG)", "transparent"), ("Environnement (HDRI)", "environment")];
    private static readonly (string Label, int Width, int Height)[] Formats = [("Carré 1:1", 1, 1), ("Paysage 4:3", 4, 3), ("Portrait 3:4", 3, 4), ("Large 16:9", 16, 9)];
    private static readonly string[] TabNames = ["Pièce", "Matière", "Ambiance", "Caméra"];
    /// <summary>A render step: long side in pixels, samples and engine.</summary>
    private readonly record struct Pass(int Side, int Samples, string Engine)
    {
        // Live: small and without ray tracing, so the image follows the mouse; Cycles then refines in two steps. Each Cycles pass
        // costs about 0.3 s of set-up whatever its samples (measured, 300k triangles, RTX), so a middle step only delayed the
        // sharp image (2.7 s -> 2.0 s once the camera rests).
        public static readonly Pass Live = new(400, 8, "eevee");
        public static readonly Pass[] Refine = [new(640, 8, "cycles"), new(960, 128, "cycles")];
    }

    private readonly Func<IReadOnlyList<RenderColor>> _loadColors;
    private readonly RenderPreferencesStore _store;
    private IMeshViewport _viewport;
    private readonly Grid _frame = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly OrientationGizmo _gizmo = new() { IsVisible = false };
    private readonly SelectionHighlight _highlight = new();
    private readonly TextBlock _viewHelp = new() { Text = "Glisser : tourner · Clic droit : déplacer · Molette : zoomer", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _emptyState = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
    private readonly Border _emptyCover;
    private readonly Grid _stageHost = new() { ClipToBounds = true };
    private readonly TextBlock _status = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly ComboBox _finish = Choice(Finishes.Select(f => f.Label));
    private readonly CheckBox _glowDark = new() { Content = "Dans le noir (lumières éteintes)" };
    private readonly ComboBox _layer = Choice(Layers.Select(l => l.Label));
    private readonly ComboBox _scene = Choice(Scenes.Select(s => s.Label));
    private readonly TextBox _background = new();
    private readonly StackPanel _backgroundField;
    private readonly ComboBox _format = Choice(Formats.Select(f => f.Label));
    private readonly ComboBox _environment = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choisissez un environnement" };
    private readonly Slider _environmentRotation = Slider(0, 360, 15);
    private readonly StackPanel _environmentField = new() { Spacing = 12 };
    private readonly CheckBox _shadow = new() { Content = "Ombre au sol" };
    private readonly Slider _shadowStrength = Slider(0, 1, 0.05);
    private readonly Slider _shadowSoftness = Slider(0, 1, 0.05);
    private readonly Slider _lightAngle = Slider(0, 360, 15);
    private readonly Slider _lightHeight = Slider(10, 80, 5);
    private readonly StackPanel _studioLightFields = new() { Spacing = 12 };
    private readonly Button[] _tabButtons;
    private readonly Control[] _tabs;
    private readonly Button _final = new() { Content = Icons.Label("IconPlay", "Rendu final"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Annuler le rendu", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private readonly Button _save = new() { Content = Icons.Label("IconSave", "Enregistrer l’image"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _work = ProductRenderer.WorkFolder();
    private RenderPreferences _saved;

    private string? _savedEnvironment;
    private string? _blender;
    private BlenderSession? _live;
    private BlenderSession? _refine;
    private CancellationTokenSource? _refineCancel;
    private bool _blenderSearched;
    private string? _modelPath;
    private string? _stlPath;
    private MeshModel? _mesh;
    private string? _shown;
    private string? _shownTiff;
    private bool _settingMesh;
    private int _counter;
    private int _generation;
    private Task _previews = Task.CompletedTask;
    private CancellationTokenSource? _finalRender;

    /// <summary>Asks the window to file a copy of the image (temporary PNG, model path), like a capture of the 3D viewer.</summary>
    /// <summary>
    /// Files images for the window (temporary files, model path, choose the folder): in the model's project, or in a folder the
    /// user picks. Returns the folder they went to, or null when nothing was saved.
    /// </summary>
    public Func<IReadOnlyList<string>, string, bool, Task<string?>>? SaveFiles { get; set; }

    private RenderResultWindow? _resultWindow;
    /// <summary>Quality, device, file and series: shown in the render window, not on the page.</summary>
    private readonly Control _outputPanel;

    public RenderStudioView(Func<IReadOnlyList<RenderColor>> loadColors, RenderPreferencesStore? store = null)
    {
        _loadColors = loadColors;
        _store = store ?? new RenderPreferencesStore();
        var saved = _saved = _store.Load();
        _finish.SelectedIndex = Math.Clamp(saved.Finish, 0, Finishes.Length - 1);
        _layer.SelectedIndex = Math.Clamp(saved.Layer, 0, Layers.Length - 1);
        _scene.SelectedIndex = Math.Clamp(saved.Scene, 0, Scenes.Length - 1);
        _format.SelectedIndex = Math.Clamp(saved.Format, 0, Formats.Length - 1);
        _background.Text = saved.Background;
        _environmentRotation.Value = saved.EnvironmentRotation;
        _savedEnvironment = saved.Environment;
        _glowDark.IsChecked = saved.GlowDark;
        _shadow.IsChecked = saved.Shadow;
        _shadowStrength.Value = saved.ShadowStrength;
        _shadowSoftness.Value = saved.ShadowSoftness;
        _lightAngle.Value = saved.LightAngle;
        _lightHeight.Value = saved.LightHeight;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            e.DragEffects = path is not null && ThreeDFileSupport.IsPreviewable(path) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            try { await LoadModelAsync(ModelFileSelection.Select(paths)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { ShowStatus(exception.Message); }
        });

        // The 3D view and the image share a frame with the image's proportions, so what is framed live is what Blender renders.
        _viewport = CreateViewport();
        _frame.Children.Add(_viewport.Visual);
        _frame.Children.Add(_image);
        _frame.Children.Add(_highlight);
        _frame.Children.Add(_gizmo);
        _frame.Children.Add(new Border
        {
            Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12),
            IsHitTestVisible = false, Padding = new Thickness(12, 4),
            Child = _viewHelp
        });
        _emptyCover = new Border { Background = (IBrush?)Application.Current?.FindResource("CanvasBrush"), Child = _emptyState };
        _stageHost.Children.Add(_frame);
        _stageHost.Children.Add(_emptyCover);
        _stageHost.SizeChanged += (_, _) => FitFrame();
        var stageFrame = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = _stageHost
        };
        var addPart = new Button { Content = Icons.Label("IconPlus", "Ajouter une pièce"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(addPart, "Ajouter une pièce à la scène");
        ToolTip.SetTip(addPart, "Ajouter un modèle STL, OBJ ou 3MF à côté des pièces déjà présentes.");
        addPart.Click += async (_, _) => await BrowseAddAsync();
        var stageColumn = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 8 };
        stageColumn.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 4, Children = { addPart, _transformMode, BuildSceneFileMenu() } });
        Grid.SetRow(stageFrame, 1);
        stageColumn.Children.Add(stageFrame);
        Grid.SetRow(_progress, 2);
        stageColumn.Children.Add(_progress);
        Grid.SetRow(_status, 3);
        stageColumn.Children.Add(_status);

        foreach (var box in new[] { _layer, _environment }) box.SelectionChanged += (_, _) => SettingsChanged();
        _finish.SelectionChanged += (_, _) => { _glowDark.IsVisible = Finishes[_finish.SelectedIndex].Key == "glow"; MarkSwatch(); SettingsChanged(); };
        _glowDark.IsVisible = Finishes[_finish.SelectedIndex].Key == "glow";
        foreach (var box in new[] { _glowDark, _shadow }) box.IsCheckedChanged += (_, _) => SettingsChanged();
        _format.SelectionChanged += (_, _) => { FitFrame(); SettingsChanged(); };
        _scene.SelectionChanged += (_, _) => { ShowSceneFields(); SettingsChanged(); };
        foreach (var slider in new[] { _environmentRotation, _shadowStrength, _shadowSoftness, _lightAngle, _lightHeight })
            slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) SettingsChanged(); };
        _background.LostFocus += (_, _) => SettingsChanged();
        _background.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SettingsChanged(); e.Handled = true; } };
        _backgroundField = FormKit.Field("Couleur du fond", _background, hint: "Code #RRGGBB");
        var addEnvironment = new Button { Content = Icons.Label("IconOpen", "Ajouter un fichier HDR ou EXR"), HorizontalAlignment = HorizontalAlignment.Stretch };
        addEnvironment.Click += async (_, _) => await BrowseEnvironmentAsync();
        _environmentField.Children.Add(FormKit.Field("Environnement", _environment, hint: "Ceux fournis avec Blender, ou vos images HDRI (Poly Haven par exemple)."));
        _environmentField.Children.Add(addEnvironment);
        _environmentField.Children.Add(ResettableSlider("Rotation de l’environnement", _environmentRotation, RenderPreferences.Default.EnvironmentRotation, "L’ombre suit la lumière de l’environnement."));
        _studioLightFields.Children.Add(ResettableSlider("Direction de la lumière", _lightAngle, RenderPreferences.Default.LightAngle, "Tourne les lumières autour de la pièce, et l’ombre avec."));
        _studioLightFields.Children.Add(ResettableSlider("Hauteur de la lumière", _lightHeight, RenderPreferences.Default.LightHeight, "Basse : ombre longue ; haute : ombre courte sous la pièce."));
        _final.Click += (_, _) => OpenRenderWindow();
        ToolTip.SetTip(_final, "Ouvre la fenêtre de rendu : qualité, fichier et séries, puis « Lancer le rendu ».");
        _cancel.Click += (_, _) => _finalRender?.Cancel();
        _save.Click += (_, _) => Save();

        _tabs = [BuildPartTab(), BuildMaterialTab(), BuildAmbianceTab(), BuildCameraTab()];
        _outputPanel = BuildOutputTab();
        var tabRow = new UniformGrid { Columns = TabNames.Length, Margin = new Thickness(0, 0, 0, 4) };
        _tabButtons = TabNames.Select((name, index) =>
        {
            var chip = new Button { Content = name, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            chip.Click += (_, _) => { ShowTab(index); SavePreferences(); };
            tabRow.Children.Add(chip);
            return chip;
        }).ToArray();
        var tabBody = new Panel();
        foreach (var tab in _tabs)
            tabBody.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new Border { Padding = new Thickness(0, 4, 12, 4), Child = tab } });
        var actions = new StackPanel { Spacing = 8, Children = { _final, _cancel, _save } };
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        panel.Children.Add(tabRow);
        Grid.SetRow(tabBody, 1);
        panel.Children.Add(tabBody);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,380"), ColumnSpacing = 16 };
        root.Children.Add(stageColumn);
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Content = root;
        DetachedFromVisualTree += (_, _) => { _live?.Dispose(); _refine?.Dispose(); };
        ShowSceneFields();
        ShowTab(Math.Clamp(saved.Tab, 0, TabNames.Length - 1));
        ShowEmptyState();
        InitializeMaterial();
        InitializePart();
        InitializeOutput();
        InitializePaint();
        InitializePause();
    }

    private static Control ResettableSlider(string label, Slider slider, double defaultValue, string hint)
    {
        var reset = new Button { Content = "Défaut", Classes = { "ghost" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(reset, "Rétablir : " + label);
        ToolTip.SetTip(reset, "Rétablir la valeur par défaut : " + defaultValue.ToString(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));
        reset.Click += (_, _) => slider.Value = defaultValue;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(slider);
        Grid.SetColumn(reset, 1);
        row.Children.Add(reset);
        return FormKit.Field(label, row, hint: hint);
    }

    private Control BuildAmbianceTab()
    {
        var reset = new Button { Content = "Réinitialiser les ombres et la lumière", HorizontalAlignment = HorizontalAlignment.Stretch };
        reset.Click += (_, _) =>
        {
            var defaults = RenderPreferences.Default;
            _shadow.IsChecked = defaults.Shadow;
            _shadowStrength.Value = defaults.ShadowStrength;
            _shadowSoftness.Value = defaults.ShadowSoftness;
            _lightAngle.Value = defaults.LightAngle;
            _lightHeight.Value = defaults.LightHeight;
        };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.Field("Ambiance", _scene), _backgroundField, _environmentField,
                FormKit.SectionLabel("Ombres et lumière"), _shadow,
                ResettableSlider("Intensité de l’ombre", _shadowStrength, RenderPreferences.Default.ShadowStrength, "Légère à gauche, marquée à droite."),
                ResettableSlider("Douceur de l’ombre", _shadowSoftness, RenderPreferences.Default.ShadowSoftness, "Nette à gauche (petite lumière), diffuse à droite (grande lumière)."),
                _studioLightFields, reset,
                new TextBlock { Text = "Fond transparent ou HDRI : l’ombre au sol apparaît dans le rendu HD, pas dans l’aperçu en direct.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private void ShowTab(int index)
    {
        for (var i = 0; i < _tabs.Length; i++)
        {
            ((Control)_tabs[i].Parent!.Parent!).IsVisible = i == index;
            if (i == index) _tabButtons[i].Classes.Add("selected"); else _tabButtons[i].Classes.Remove("selected");
        }
        if (index != 0) _transformMode.IsChecked = false;
        UpdateRotationGizmo();
    }

    private int CurrentTab => Array.FindIndex(_tabButtons, b => b.Classes.Contains("selected"));

    private IMeshViewport CreateViewport()
    {
        IMeshViewport viewport;
        try
        {
            var gpu = new GpuViewportHost();
            gpu.Failed += _ => Dispatcher.UIThread.Post(() => SwapViewport(new MeshViewport()));
            viewport = gpu;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { viewport = new MeshViewport(); }
        Hook(viewport);
        return viewport;
    }

    private void Hook(IMeshViewport viewport)
    {
        viewport.ShowGrid = false;
        viewport.CameraChanged += CameraMoved;
        viewport.Clicked += FaceClicked;
        AutomationProperties.SetName(viewport.Visual, "Cadrage du rendu : glisser pour tourner, clic droit pour déplacer, molette pour zoomer");
        if (PreviewColorParser.TryParse(CurrentHex, out var color)) viewport.ModelColor = color;
        _gizmo.Camera = viewport.Camera;
    }

    /// <summary>The graphics card view failed: the processor view takes over with the same model and camera.</summary>
    private void SwapViewport(IMeshViewport next)
    {
        var camera = _viewport.Camera;
        _frame.Children.Remove(_viewport.Visual);
        _viewport.CameraChanged -= CameraMoved;
        _viewport.Clicked -= FaceClicked;
        _viewport = next;
        Hook(next);
        (next.Camera.Yaw, next.Camera.Pitch, next.Camera.Zoom, next.Camera.PanX, next.Camera.PanY) = (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY);
        _frame.Children.Insert(0, next.Visual);
        next.SetMesh(_mesh);
    }

    /// <summary>The frame takes the largest size with the image's proportions.</summary>
    private void FitFrame()
    {
        var (_, ratioWidth, ratioHeight) = Formats[_format.SelectedIndex];
        var available = _stageHost.Bounds.Size;
        if (available.Width <= 0 || available.Height <= 0) return;
        var scale = Math.Min(available.Width / ratioWidth, available.Height / ratioHeight);
        _frame.Width = ratioWidth * scale;
        _frame.Height = ratioHeight * scale;
    }

    /// <summary>Called on each visit: finds Blender once and refreshes the filament colours of the workshop.</summary>
    public async Task ActivateAsync()
    {
        if (!_blenderSearched)
        {
            _blender = await Task.Run(BlenderLocator.Detect);
            _blenderSearched = true;
            _live?.Dispose();
            _refine?.Dispose();
            (_live, _refine) = _blender is null ? (null, null) : (new BlenderSession(_blender), new BlenderSession(_blender));
            // Both Blenders start now, while the user picks a model: the first images do not wait for them.
            if (_live is not null && _refine is not null) { _ = _live.WarmUpAsync("eevee"); WarmRefine(_refine); }
            _final.IsEnabled = _live is not null && _stlPath is not null;
            if (_blender is { } blender) _environment.ItemsSource = (await Task.Run(() => BlenderLocator.Environments(blender))).ToList();
            if (_savedEnvironment is { } saved && File.Exists(saved)) SelectEnvironment(saved);
            ShowEmptyState();
        }
        ResumeEngines();
        try { _workshopColors = await Task.Run(_loadColors); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { _workshopColors = []; }
        RebuildSwatches();
    }

    private async Task BrowseAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir le modèle à mettre en scène",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Modèles 3D (STL, OBJ, 3MF)") { Patterns = ["*.stl", "*.obj", "*.3mf"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } local) await LoadModelAsync(local);
    }

    /// <summary>The camera moves: the live render follows it (the 3D view underneath shows only until the first image).</summary>
    private void CameraMoved()
    {
        _gizmo.InvalidateVisual();
        if (_mesh is null || _settingMesh) return;
        _highlight.Hide();
        // Paused: the last image would hide the new framing, so the simple 3D view shows through until the preview resumes.
        if (_paused) _image.Opacity = 0;
        // The studio has a floor: the camera stops at its level, in the 3D view as in the render.
        if (_viewport.Camera.Pitch < 0) { _viewport.Camera.Pitch = 0; _viewport.RequestRender(true); }
        MarkCamera();
        SettingsChanged();
    }

    private void SettingsChanged()
    {
        if (_stlPath is null || _live is null) return;
        _generation++;
        // Paused like KeyShot: nothing is computed, the changes are rendered when the preview resumes.
        if (_paused) { _refineCancel?.Cancel(); return; }
        // The HD render has the graphics card to itself: changes wait for it to end.
        if (_finalRender is not null) return;
        _refineCancel?.Cancel();
        RunPreviews();
    }

    private async void RunPreviews() => await RunPreviewsAsync();

    /// <summary>
    /// Progressive preview, like KeyShot. While the settings change, a Blender kept for EEVEE (real time) renders small images
    /// one after the other; as soon as they rest, a second Blender kept for Cycles refines the view in steps (8, then 128
    /// samples, denoised on the graphics card), each image replacing the previous one. A change stops the refining Blender at once
    /// (it restarts in the background) so the live images never wait behind it.
    /// </summary>
    private Task RunPreviewsAsync() => _paused ? Task.CompletedTask : _previews = _previews.IsCompleted ? PreviewLoopAsync() : _previews;

    private async Task PreviewLoopAsync()
    {
        if (_live is null || _refine is null || _stlPath is null) return;
        while (true)
        {
            if (_gizmo.IsDragging) return;
            var generation = _generation;
            if (!await RenderPassAsync(Pass.Live, _live, CancellationToken.None)) return;
            if (generation != _generation) continue;
            // A short rest before refining, so a slow drag does not start Cycles between two moves.
            await Task.Delay(120);
            if (generation != _generation) continue;
            using var cancel = _refineCancel = new CancellationTokenSource();
            var watch = Stopwatch.StartNew();
            foreach (var step in Pass.Refine)
            {
                if (generation != _generation || cancel.IsCancellationRequested) break;
                ShowStatus($"Affinage de l’aperçu ({step.Samples} échantillons)…");
                if (!await RenderPassAsync(step, _refine, cancel.Token))
                {
                    if (cancel.IsCancellationRequested) { _ = _refine.WarmUpAsync("cycles"); break; }
                    return;
                }
            }
            if (ReferenceEquals(_refineCancel, cancel)) _refineCancel = null;
            if (generation != _generation) continue;
            SavePreferences();
            var seconds = watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            ShowStatus($"Aperçu affiné en {seconds} s. Bougez la caméra dans l’image ; « Rendu final » donne l’image en grand.");
            return;
        }
    }

    private string RenderSummary() => $"{RenderModelKey.Name(_modelPath ?? "")} · {Qualities[Math.Max(0, _quality.SelectedIndex)].Label} · {Scenes[_scene.SelectedIndex].Label} · {Formats[_format.SelectedIndex].Label}";

    private string SuggestedName() => $"{RenderModelKey.Name(_modelPath ?? "rendu")} - rendu {DateTime.Now:yyyy-MM-dd HHmmss}";

    /// <summary>
    /// The render window (as in KeyShot): opened by « Rendu final » with the settings on the right and the current preview;
    /// the render starts from its « Lancer le rendu ».
    /// </summary>
    private RenderResultWindow? OpenRenderWindow()
    {
        if (_modelPath is null || _mesh is null) return null;
        var window = _resultWindow;
        if (window is null)
        {
            window = _resultWindow = new RenderResultWindow("Rendu · " + RenderModelKey.Name(_modelPath), _outputPanel) { Icon = (TopLevel.GetTopLevel(this) as Window)?.Icon };
            window.CancelRequested += () => _finalRender?.Cancel();
            window.RenderRequested += async () => await RenderFinalAsync();
            window.SaveRequested += async (png, tiff, name, chooseFolder) => await SaveFinalAsync(window, png, tiff, CurrentOutput, name, chooseFolder);
            window.Closed += (_, _) => { if (ReferenceEquals(_resultWindow, window)) _resultWindow = null; _finalRender?.Cancel(); };
            if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show();
        }
        else window.Activate();
        window.Setup(RenderSummary(), SuggestedName(), _shown);
        return window;
    }

    /// <summary>The final render in its window: a quick pass shown at once, then the full image; the page keeps its preview.</summary>
    private async Task RenderFinalAsync()
    {
        if (_finalRender is not null || OpenRenderWindow() is not { } window) return;
        var quality = Qualities[_quality.SelectedIndex];
        window.Start(RenderSummary(), SuggestedName());
        string? done = null, doneTiff = null;
        var steps = FinalSteps(quality.Pass);
        var finished = await RunExclusiveAsync(async (cancel, progress) =>
        {
            // Like KeyShot, the image appears quickly and refines: a short pass first (shown in the window), then the full one.
            var before = 0;
            string? preview = null;
            foreach (var step in steps)
            {
                var last = step == steps[^1];
                var samples = step.Samples;
                var output = Path.Combine(_work, $"final-{++_counter}.png");
                var tiff = last && Outputs[Math.Max(0, _outputFormat.SelectedIndex)].Format == "tiff" ? Path.ChangeExtension(output, ".tif") : null;
                if (ReadSettings(step, _mesh!, tiff: tiff) is not { } settings) return false;
                var label = last ? "Rendu" : "Aperçu rapide";
                var start = before;
                window.Samples(label, 0, samples);
                await SessionFor(quality.Pass).RenderAsync(_stlPath!, settings, output, cancel, new Progress<double>(f =>
                {
                    window.Samples(label, (int)Math.Round(f * samples), samples);
                    progress((start + f * samples) / steps.Sum(p => p.Samples), 1, 1);
                }));
                before += samples;
                if (preview is not null) TryDelete(preview);
                if (last) (done, doneTiff) = (output, tiff);
                else { window.ShowPreview(output); preview = output; }
            }
            ShowStatus($"Rendu {quality.Label} terminé : il est dans sa fenêtre.");
            return true;
        }, $"Rendu {quality.Label}", (text, fraction) => window.Progress(text, fraction));
        if (finished && done is not null) { window.ShowResult(done, doneTiff); await EstimateSizeAsync(); }
        else window.Stopped(_status.Text ?? "Rendu arrêté.");
    }

    /// <summary>
    /// The passes of a final render: a quick one (16 samples, at most 1024 px so it shows in seconds even for « Très haute »)
    /// before the full one; EEVEE and short renders go in one pass.
    /// </summary>
    private static Pass[] FinalSteps(Pass pass)
        => pass.Engine == "cycles" && pass.Samples > 32 ? [pass with { Side = Math.Min(pass.Side, 1024), Samples = 16 }, pass] : [pass];

    /// <summary>Writes the final image with the window's choices, then files it; the window learns where it went.</summary>
    private async Task SaveFinalAsync(RenderResultWindow window, string png, string? tiff, OutputOptions options, string name, bool chooseFolder)
    {
        if (_modelPath is not { } model || SaveFiles is not { } save) return;
        var folder = Directory.CreateDirectory(Path.Combine(_work, Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = await Task.Run(() =>
            {
                if (options.Format == "tiff" && tiff is not null && File.Exists(tiff))
                {
                    var copy = Path.Combine(folder, name + ".tif");
                    File.Copy(tiff, copy);
                    return copy;
                }
                var target = Path.Combine(folder, name + (options.Format == "tiff" ? ".png" : options.Extension));
                File.WriteAllBytes(target, RenderOutput.Encode(png, options.Format == "tiff" ? options with { Format = "png" } : options));
                return target;
            });
            if (await save([file], model, chooseFolder) is { } where) window.MarkSaved(where);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException) { window.Stopped(exception.Message); }
    }

    /// <summary>Warms the Cycles Blender, then shows the graphics card it found in the Rendu tab.</summary>
    private async void WarmRefine(BlenderSession refine)
    {
        await refine.WarmUpAsync("cycles");
        ShowOutputHints();
    }

    private BlenderSession SessionFor(Pass pass) => pass.Engine == "eevee" ? _live! : _refine!;

    /// <summary>
    /// Runs a final render or a series with the graphics card to itself: the preview and the 3D view pause and the camera is
    /// locked; the status shows the percentage and the time left; « Annuler » stops it. Settings changed meanwhile apply after.
    /// </summary>
    private async Task<bool> RunExclusiveAsync(Func<CancellationToken, Action<double, int, int>, Task<bool>> work, string label, Action<string, double>? mirror = null)
    {
        if (_live is null || _refine is null || _stlPath is null || _mesh is null) return false;
        _refineCancel?.Cancel();
        await _previews;
        using var cancel = _finalRender = new CancellationTokenSource();
        var generation = _generation;
        _cancel.IsVisible = true;
        _final.IsEnabled = _runSeries.IsEnabled = false;
        _viewport.Visual.IsVisible = false;
        _frame.IsHitTestVisible = false;
        _progress.IsIndeterminate = false;
        _progress.Value = 0;
        _progress.IsVisible = true;
        var watch = Stopwatch.StartNew();
        var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        void Progress(double fraction, int image, int images)
        {
            var overall = Math.Clamp((image - 1 + fraction) / images, 0, 1);
            _progress.Value = overall * 100;
            var left = overall > 0.03 ? TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds * (1 - overall) / overall) : (TimeSpan?)null;
            _status.Text = (images > 1 ? $"{label} · image {image}/{images} · " : label + " · ") + overall.ToString("0 %", french)
                + (left is { } l ? $" · environ {(l.TotalMinutes >= 1 ? $"{(int)l.TotalMinutes} min {l.Seconds:00}" : $"{Math.Max(1, l.Seconds)} s")} restantes" : string.Empty)
                + ". L’aperçu est en pause.";
            mirror?.Invoke(_status.Text, overall);
        }
        Progress(0, 1, 1);
        SavePreferences();
        try { return await work(cancel.Token, Progress); }
        catch (OperationCanceledException) { ShowStatus("Rendu annulé."); return false; }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(exception.Message);
            return false;
        }
        finally
        {
            _cancel.IsVisible = false;
            _final.IsEnabled = _runSeries.IsEnabled = true;
            _viewport.Visual.IsVisible = true;
            _frame.IsHitTestVisible = true;
            _progress.IsVisible = false;
            _progress.IsIndeterminate = true;
            if (ReferenceEquals(_finalRender, cancel)) _finalRender = null;
            if (cancel.IsCancellationRequested) _ = _refine.WarmUpAsync("cycles");
            if (generation != _generation) RunPreviews();
        }
    }

    /// <summary>Shows a rendered image; the previous one shown is deleted.</summary>
    private void ShowImage(string output)
    {
        Bitmap bitmap;
        using (var stream = File.OpenRead(output)) bitmap = new Bitmap(stream);
        (_image.Source as IDisposable)?.Dispose();
        _image.Source = bitmap;
        _image.Opacity = 1;
        if (_shown is { } previous && previous != output) TryDelete(previous);
        if (_shownTiff is { } tiff) TryDelete(tiff);
        _shownTiff = null;
        _shown = output;
        _save.IsEnabled = true;
    }

    /// <summary>One preview render, shown as soon as it is ready. False when it failed or was cancelled.</summary>
    private async Task<bool> RenderPassAsync(Pass pass, BlenderSession session, CancellationToken cancellationToken)
    {
        var sceneVersion = _sceneVersion;
        if (_stlPath is not { } stl || _mesh is not { } mesh) return false;
        if (ReadSettings(pass, mesh) is not { } settings) return false;
        var output = Path.Combine(_work, $"render-{++_counter}.png");
        if (_image.Source is null) ShowStatus("Premier aperçu…", busy: true);
        try
        {
            await session.RenderAsync(stl, settings, output, cancellationToken);
            if (sceneVersion == _sceneVersion && !_gizmo.IsDragging) ShowImage(output);
            else TryDelete(output);
            _progress.IsVisible = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(output);
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(exception.Message);
            return false;
        }
    }

    private RenderSettings? ReadSettings(Pass pass, MeshModel mesh, RenderColor? color = null, SavedCamera? view = null, string? tiff = null)
    {
        var hex = color?.Hex ?? CurrentHex;
        var finish = color?.Finish ?? Finishes[_finish.SelectedIndex].Key;
        if (!PreviewColorParser.TryParse(hex, out _)) { ShowStatus("Couleur du filament invalide : saisissez un code comme #D93A3A."); return null; }
        var scene = Scenes[_scene.SelectedIndex].Key;
        if (scene == "color" && !PreviewColorParser.TryParse(_background.Text?.Trim(), out _)) { ShowStatus("Couleur du fond invalide : saisissez un code comme #2F6FD6."); return null; }
        var environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? string.Empty;
        if (scene == "environment" && !File.Exists(environment)) { ShowStatus("Choisissez un environnement, ou ajoutez votre image HDR ou EXR."); return null; }
        var (_, ratioWidth, ratioHeight) = Formats[_format.SelectedIndex];
        var (side, samples, engine) = (pass.Side, pass.Samples, pass.Engine);
        var (width, height) = ratioWidth >= ratioHeight ? (side, side * ratioHeight / ratioWidth) : (side * ratioWidth / ratioHeight, side);
        var current = _viewport.Camera;
        var camera3d = view is null ? current : new ViewCamera { Yaw = view.Yaw, Pitch = view.Pitch, Zoom = view.Zoom, PanX = view.PanX, PanY = view.PanY };
        var (camera, rotation) = RenderCamera.From(mesh, camera3d, width, height);
        return new RenderSettings(hex, finish, Layers[_layer.SelectedIndex].Height, scene, _background.Text?.Trim() ?? "#FFFFFF",
            rotation, 0, width, height, samples, environment, _environmentRotation.Value, camera, engine,
            _shadow.IsChecked == true, _shadowStrength.Value, _shadowSoftness.Value, _lightAngle.Value, _lightHeight.Value, _glowDark.IsChecked == true,
            null, 0,
            "auto", tiff, _zoneFiles.Select(z => new RenderZone(z.File, z.Zone.Hex, z.Zone.Finish)).ToList(), GroundZ: 0);
    }

    private void ShowSceneFields()
    {
        var scene = Scenes[_scene.SelectedIndex].Key;
        _backgroundField.IsVisible = scene == "color";
        _environmentField.IsVisible = scene == "environment";
        _studioLightFields.IsVisible = scene != "environment";
    }

    /// <summary>Selects an environment by its file, adding it to the list when it is one of the user's own images.</summary>
    private void SelectEnvironment(string path)
    {
        var list = _environment.ItemsSource as List<RenderEnvironment> ?? [];
        var known = list.FirstOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (known is null)
        {
            known = new RenderEnvironment(Path.GetFileNameWithoutExtension(path) + " (votre image)", path);
            list = [.. list, known];
            _environment.ItemsSource = list;
        }
        _environment.SelectedItem = known;
    }

    private async Task BrowseEnvironmentAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir une image d’environnement",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images HDRI (HDR, EXR)") { Patterns = ["*.hdr", "*.exr"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } local && BlenderLocator.IsEnvironmentFile(local)) SelectEnvironment(local);
    }

    private void SavePreferences()
    {
        var camera = _viewport.Camera;
        var orientations = new Dictionary<string, float[]>(_saved.Orientations);
        if (_modelPath is { } model) orientations[model] = [_turn.X, _turn.Y, _turn.Z, _turn.W];
        foreach (var other in _parts.Where(p => p.Path != _modelPath)) orientations[other.Path] = [other.Turn.X, other.Turn.Y, other.Turn.Z, other.Turn.W];
        _saved = _saved with
        {
            CustomColors = _customColors.ToList(), HiddenColors = _hiddenColors.ToList(), Hex = CurrentHex, Finish = _finish.SelectedIndex,
            Layer = _layer.SelectedIndex, Scene = _scene.SelectedIndex, Background = _background.Text?.Trim() ?? RenderPreferences.Default.Background,
            Format = _format.SelectedIndex, Environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? _savedEnvironment,
            EnvironmentRotation = _environmentRotation.Value, Yaw = camera.Yaw, Pitch = camera.Pitch, Zoom = camera.Zoom, Tab = Math.Max(0, CurrentTab),
            RotationStep = _gizmo.Step, Shadow = _shadow.IsChecked == true, ShadowStrength = _shadowStrength.Value, ShadowSoftness = _shadowSoftness.Value,
            LightAngle = _lightAngle.Value, LightHeight = _lightHeight.Value, GlowDark = _glowDark.IsChecked == true, Orientations = orientations,
            Quality = Math.Max(0, _quality.SelectedIndex), OutputFormat = CurrentOutput.Format,
            Paused = _paused, OutputQuality = _outputQuality.Value, MaxCompression = _maxCompression.IsChecked == true, MaxSide = CurrentOutput.MaxSide, ContactSheet = _contactSheet.IsChecked == true
        };
        _store.Save(_saved);
    }

    /// <summary>Writes the image on screen in the chosen format and size, then asks the window to file it.</summary>
    private async void Save()
    {
        if (_shown is not { } shown || _modelPath is null) return;
        var options = CurrentOutput;
        var folder = Directory.CreateDirectory(Path.Combine(_work, Guid.NewGuid().ToString("N"))).FullName;
        var name = $"{Path.GetFileNameWithoutExtension(_modelPath)} - rendu {DateTime.Now:yyyy-MM-dd HHmmss}";
        try
        {
            var file = await Task.Run(() =>
            {
                if (options.Format == "tiff" && _shownTiff is { } tiff && File.Exists(tiff))
                {
                    var copy = Path.Combine(folder, name + ".tif");
                    File.Copy(tiff, copy);
                    return copy;
                }
                var target = Path.Combine(folder, name + (options.Format == "tiff" ? ".png" : options.Extension));
                File.WriteAllBytes(target, RenderOutput.Encode(shown, options.Format == "tiff" ? options with { Format = "png" } : options));
                return target;
            });
            if (options.Format == "tiff" && !file.EndsWith(".tif", StringComparison.Ordinal)) ShowStatus("Le TIFF 16 bits vient du rendu final : cet aperçu est enregistré en PNG.");
            if (SaveFiles is { } save) await save([file], _modelPath, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException) { ShowStatus(exception.Message); }
    }

    private void ShowEmptyState()
    {
        _emptyState.Children.Clear();
        _emptyCover.IsVisible = _mesh is null;
        if (_blenderSearched && _blender is null)
        {
            _emptyState.Children.Add(new TextBlock { Text = "Blender n’est pas installé", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            _emptyState.Children.Add(new TextBlock { Text = "Le rendu produit utilise Blender, gratuit. Installez-le depuis blender.org, puis revenez sur cette page.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            var download = new Button { Content = "Télécharger Blender", Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            download.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://www.blender.org/download/"));
                _blenderSearched = false;
            };
            _emptyState.Children.Add(download);
            _emptyCover.IsVisible = true;
            return;
        }
        _emptyState.Children.Add(new TextBlock { Text = "Déposez un fichier STL, OBJ ou 3MF ici", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        _emptyState.Children.Add(new TextBlock { Text = "Cadrez à la souris dans l’image, choisissez la couleur et l’ambiance : le rendu suit.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var browse = new Button { Content = Icons.Label("IconOpen", "Ouvrir un modèle"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        browse.Click += async (_, _) => await BrowseAsync();
        _emptyState.Children.Add(browse);
    }

    private void ShowStatus(string text, bool busy = false)
    {
        _status.Text = text;
        _progress.IsVisible = busy;
    }

    private static ComboBox Choice(IEnumerable<string> labels)
        => new() { ItemsSource = labels.ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static Slider Slider(double minimum, double maximum, double step)
        => new() { Minimum = minimum, Maximum = maximum, TickFrequency = step, IsSnapToTickEnabled = true };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

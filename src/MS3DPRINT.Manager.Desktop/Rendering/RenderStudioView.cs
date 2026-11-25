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
    private static readonly (string Label, string Key)[] Finishes = [("Mat", "matte"), ("Satiné / soie", "silk"), ("Brillant", "glossy"), ("Phosphorescent", "glow")];
    private static readonly (string Label, double Height)[] Layers = [("Lisse, sans lignes", 0), ("0,12 mm", 0.12), ("0,16 mm", 0.16), ("0,20 mm", 0.2), ("0,28 mm", 0.28)];
    private static readonly (string Label, string Key)[] Scenes = [("Studio clair", "light"), ("Studio sombre", "dark"), ("Fond de couleur", "color"), ("Fond transparent (PNG)", "transparent"), ("Environnement (HDRI)", "environment")];
    private static readonly (string Label, int Width, int Height)[] Formats = [("Carré 1:1", 1, 1), ("Paysage 4:3", 4, 3), ("Portrait 3:4", 3, 4), ("Large 16:9", 16, 9)];
    private static readonly string[] TabNames = ["Pièce", "Matière", "Ambiance", "Caméra"];
    private enum Pass { Live, Final }

    private readonly Func<IReadOnlyList<RenderColor>> _loadColors;
    private readonly RenderPreferencesStore _store;
    private IMeshViewport _viewport;
    private readonly Grid _frame = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly OrientationGizmo _gizmo = new() { IsVisible = false };
    private readonly StackPanel _emptyState = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
    private readonly Border _emptyCover;
    private readonly Grid _stageHost = new() { ClipToBounds = true };
    private readonly TextBlock _status = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly TextBlock _modelName = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
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
    private readonly Button _final = new() { Content = Icons.Label("IconPlay", "Rendu HD"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Annuler le rendu HD", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private readonly Button _save = new() { Content = Icons.Label("IconSave", "Enregistrer l’image"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _work = ProductRenderer.WorkFolder();
    private RenderPreferences _saved;

    private string? _savedEnvironment;
    private string? _blender;
    private BlenderSession? _session;
    private bool _blenderSearched;
    private string? _modelPath;
    private string? _stlPath;
    private MeshModel? _mesh;
    private string? _shown;
    private int _counter;
    private int _generation;
    private Task _previews = Task.CompletedTask;
    private CancellationTokenSource? _finalRender;

    /// <summary>Asks the window to file a copy of the image (temporary PNG, model path), like a capture of the 3D viewer.</summary>
    public event Action<string, string>? SaveRequested;

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
        _frame.Children.Add(_gizmo);
        _frame.Children.Add(new Border
        {
            Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12),
            IsHitTestVisible = false, Padding = new Thickness(12, 4),
            Child = new TextBlock { Text = "Glisser : tourner · Clic droit : déplacer · Molette : zoomer", Classes = { "caption" } }
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
        var stageColumn = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 8 };
        stageColumn.Children.Add(stageFrame);
        Grid.SetRow(_progress, 1);
        stageColumn.Children.Add(_progress);
        Grid.SetRow(_status, 2);
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
        _environmentField.Children.Add(FormKit.Field("Rotation de l’environnement", _environmentRotation, hint: "L’ombre suit la lumière de l’environnement."));
        _studioLightFields.Children.Add(FormKit.Field("Direction de la lumière", _lightAngle, hint: "Tourne les lumières autour de la pièce, et l’ombre avec."));
        _studioLightFields.Children.Add(FormKit.Field("Hauteur de la lumière", _lightHeight, hint: "Basse : ombre longue ; haute : ombre courte sous la pièce."));
        _final.Click += async (_, _) => await RenderFinalAsync();
        _cancel.Click += (_, _) => _finalRender?.Cancel();
        _save.Click += (_, _) => Save();

        _tabs = [BuildPartTab(), BuildMaterialTab(), BuildAmbianceTab(), BuildCameraTab()];
        var tabRow = new WrapPanel();
        _tabButtons = TabNames.Select((name, index) =>
        {
            var chip = new Button { Content = name, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
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
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        root.Children.Add(stageColumn);
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Content = root;
        DetachedFromVisualTree += (_, _) => _session?.Dispose();
        ShowSceneFields();
        ShowTab(Math.Clamp(saved.Tab, 0, TabNames.Length - 1));
        ShowEmptyState();
        InitializeMaterial();
        InitializePart();
    }

    private Control BuildAmbianceTab() => new StackPanel
    {
        Spacing = 12,
        Children =
        {
            FormKit.Field("Ambiance", _scene), _backgroundField, _environmentField,
            FormKit.SectionLabel("Ombres et lumière"), _shadow,
            FormKit.Field("Intensité de l’ombre", _shadowStrength, hint: "Légère à gauche, marquée à droite."),
            FormKit.Field("Douceur de l’ombre", _shadowSoftness, hint: "Nette à gauche (petite lumière), diffuse à droite (grande lumière)."),
            _studioLightFields,
            new TextBlock { Text = "Fond transparent ou HDRI : l’ombre au sol apparaît dans le rendu HD, pas dans l’aperçu en direct.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }
        }
    };

    private void ShowTab(int index)
    {
        for (var i = 0; i < _tabs.Length; i++)
        {
            ((Control)_tabs[i].Parent!.Parent!).IsVisible = i == index;
            if (i == index) _tabButtons[i].Classes.Add("selected"); else _tabButtons[i].Classes.Remove("selected");
        }
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
            _session?.Dispose();
            _session = _blender is null ? null : new BlenderSession(_blender);
            _final.IsEnabled = _session is not null && _stlPath is not null;
            if (_blender is { } blender) _environment.ItemsSource = (await Task.Run(() => BlenderLocator.Environments(blender))).ToList();
            if (_savedEnvironment is { } saved && File.Exists(saved)) SelectEnvironment(saved);
            ShowEmptyState();
        }
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
        if (_mesh is null) return;
        // The studio has a floor: the camera stops at its level, in the 3D view as in the render.
        if (_viewport.Camera.Pitch < 0) { _viewport.Camera.Pitch = 0; _viewport.RequestRender(true); }
        MarkCamera();
        SettingsChanged();
    }

    private void SettingsChanged()
    {
        if (_stlPath is null || _session is null) return;
        _generation++;
        _finalRender?.Cancel();
        RunPreviews();
    }

    private async void RunPreviews() => await RunPreviewsAsync();

    /// <summary>
    /// Live preview: EEVEE renders the latest settings again and again while they change (about a tenth of a second an image
    /// on a recent graphics card); each image is shown as it comes, the last one matches the settings at rest.
    /// </summary>
    private Task RunPreviewsAsync() => _previews = _previews.IsCompleted ? PreviewLoopAsync() : _previews;

    private async Task PreviewLoopAsync()
    {
        if (_session is null || _stlPath is null) return;
        var watch = new Stopwatch();
        int generation;
        do
        {
            generation = _generation;
            watch.Restart();
            if (!await RenderPassAsync(Pass.Live, CancellationToken.None)) return;
        } while (generation != _generation);
        SavePreferences();
        // The last image only: the first one after opening also waits for Blender to start.
        var each = watch.Elapsed.TotalSeconds.ToString("0.0#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        ShowStatus($"Aperçu en direct ({each} s par image). Bougez la caméra dans l’image ; le rendu HD donne l’image finale, plus réaliste.");
    }

    private async Task RenderFinalAsync()
    {
        using var cancel = _finalRender = new CancellationTokenSource();
        _cancel.IsVisible = true;
        var watch = Stopwatch.StartNew();
        EventHandler tick = (_, _) => _status.Text = $"Rendu HD en cours · {watch.Elapsed:m\\:ss} écoulées (souvent 15 s à 1 min).";
        _clock.Tick += tick;
        _clock.Start();
        SavePreferences();
        try { await RenderPassAsync(Pass.Final, cancel.Token); }
        finally
        {
            _clock.Tick -= tick;
            _clock.Stop();
            _cancel.IsVisible = false;
            if (ReferenceEquals(_finalRender, cancel)) _finalRender = null;
        }
    }

    /// <summary>One Blender render, shown as soon as it is ready. False when it failed or was cancelled.</summary>
    private async Task<bool> RenderPassAsync(Pass pass, CancellationToken cancellationToken)
    {
        if (_stlPath is not { } stl || _session is not { } session || _mesh is not { } mesh) return false;
        if (ReadSettings(pass, mesh) is not { } settings) return false;
        var output = Path.Combine(_work, $"render-{++_counter}.png");
        if (pass == Pass.Final) ShowStatus($"Rendu HD {settings.Width} × {settings.Height} en cours…", busy: true);
        else if (_image.Source is null) ShowStatus("Premier aperçu…", busy: true);
        var watch = Stopwatch.StartNew();
        try
        {
            await session.RenderAsync(stl, settings, output, cancellationToken);
            Bitmap bitmap;
            await using (var stream = File.OpenRead(output)) bitmap = new Bitmap(stream);
            (_image.Source as IDisposable)?.Dispose();
            _image.Source = bitmap;
            if (_shown is { } previous) TryDelete(previous);
            _shown = output;
            _save.IsEnabled = true;
            if (pass == Pass.Final)
            {
                var seconds = watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
                ShowStatus($"Rendu HD prêt ({settings.Width} × {settings.Height}, {seconds} s). Enregistrez-le dans le projet du modèle.");
            }
            else _progress.IsVisible = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(output);
            ShowStatus("Rendu HD annulé.");
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(exception.Message);
            return false;
        }
    }

    private RenderSettings? ReadSettings(Pass pass, MeshModel mesh)
    {
        if (!PreviewColorParser.TryParse(CurrentHex, out _)) { ShowStatus("Couleur du filament invalide : saisissez un code comme #D93A3A."); return null; }
        var scene = Scenes[_scene.SelectedIndex].Key;
        if (scene == "color" && !PreviewColorParser.TryParse(_background.Text?.Trim(), out _)) { ShowStatus("Couleur du fond invalide : saisissez un code comme #2F6FD6."); return null; }
        var environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? string.Empty;
        if (scene == "environment" && !File.Exists(environment)) { ShowStatus("Choisissez un environnement, ou ajoutez votre image HDR ou EXR."); return null; }
        var (_, ratioWidth, ratioHeight) = Formats[_format.SelectedIndex];
        // EEVEE (real time) for the live preview, Cycles on the graphics card for the final image.
        var (side, samples, engine) = pass == Pass.Live ? (960, 16, "eevee") : (2048, 256, "cycles");
        var (width, height) = ratioWidth >= ratioHeight ? (side, side * ratioHeight / ratioWidth) : (side * ratioWidth / ratioHeight, side);
        var (camera, rotation) = RenderCamera.From(mesh, _viewport.Camera, width, height);
        return new RenderSettings(CurrentHex, Finishes[_finish.SelectedIndex].Key, Layers[_layer.SelectedIndex].Height, scene, _background.Text?.Trim() ?? "#FFFFFF",
            rotation, 0, width, height, samples, environment, _environmentRotation.Value, camera, engine,
            _shadow.IsChecked == true, _shadowStrength.Value, _shadowSoftness.Value, _lightAngle.Value, _lightHeight.Value, _glowDark.IsChecked == true);
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
        _saved = _saved with
        {
            CustomColors = _customColors.ToList(), HiddenColors = _hiddenColors.ToList(), Hex = CurrentHex, Finish = _finish.SelectedIndex,
            Layer = _layer.SelectedIndex, Scene = _scene.SelectedIndex, Background = _background.Text?.Trim() ?? RenderPreferences.Default.Background,
            Format = _format.SelectedIndex, Environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? _savedEnvironment,
            EnvironmentRotation = _environmentRotation.Value, Yaw = camera.Yaw, Pitch = camera.Pitch, Zoom = camera.Zoom, Tab = Math.Max(0, CurrentTab),
            RotationStep = _gizmo.Step, Shadow = _shadow.IsChecked == true, ShadowStrength = _shadowStrength.Value, ShadowSoftness = _shadowSoftness.Value,
            LightAngle = _lightAngle.Value, LightHeight = _lightHeight.Value, GlowDark = _glowDark.IsChecked == true, Orientations = orientations
        };
        _store.Save(_saved);
    }

    private void Save()
    {
        if (_shown is null || _modelPath is null) return;
        // The window deletes the copy once filed, so the image on screen stays available for another save.
        var folder = Directory.CreateDirectory(Path.Combine(_work, Guid.NewGuid().ToString("N"))).FullName;
        var copy = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(_modelPath)} - rendu {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        try { File.Copy(_shown, copy); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowStatus(exception.Message); return; }
        SaveRequested?.Invoke(copy, _modelPath);
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

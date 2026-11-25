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
public sealed record RenderColor(string Label, string Hex, string Finish);

/// <summary>
/// Product render page: the camera moves with the mouse in the image (left drag turns, right drag moves, wheel zooms) and
/// Blender's real-time engine (EEVEE) renders that exact view continuously; Blender stays open between renders. The HD
/// render (Cycles on the graphics card) is asked for explicitly. The HD render is asked for explicitly; choices and custom colours are kept between visits.
/// </summary>
public sealed class RenderStudioView : UserControl
{
    private static readonly (string Label, string Key)[] Finishes = [("Mat (PLA)", "matte"), ("Soie (PLA Silk)", "silk"), ("Brillant (PETG)", "glossy")];
    private static readonly (string Label, double Height)[] Layers = [("Lisse, sans lignes", 0), ("0,12 mm", 0.12), ("0,16 mm", 0.16), ("0,20 mm", 0.2), ("0,28 mm", 0.28)];
    private static readonly (string Label, string Key)[] Scenes = [("Studio clair", "light"), ("Studio sombre", "dark"), ("Fond de couleur", "color"), ("Fond transparent (PNG)", "transparent"), ("Environnement (HDRI)", "environment")];
    private static readonly (string Label, int Width, int Height)[] Formats = [("Carré 1:1", 1, 1), ("Paysage 4:3", 4, 3), ("Portrait 3:4", 3, 4), ("Large 16:9", 16, 9)];
    private static readonly (string Label, double Yaw, double Pitch)[] Views =
        [("Trois-quarts", -0.6, 0.35), ("Face", 0, 0.1), ("Côté", -Math.PI / 2, 0.1), ("Dessus", -0.6, 1.2), ("Arrière", Math.PI, 0.25)];
    private enum Pass { Live, Final }

    private readonly Func<IReadOnlyList<RenderColor>> _loadColors;
    private readonly RenderPreferencesStore _store;
    private IMeshViewport _viewport;
    private readonly Grid _frame = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly StackPanel _emptyState = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
    private readonly Border _emptyCover;
    private readonly TextBlock _status = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly TextBlock _modelName = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _swatches = new();
    private readonly ColorPicker _picker = new() { IsAlphaEnabled = false, IsAlphaVisible = false, Width = 64, MinHeight = 40, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBox _hex = new();
    private readonly Button _keepColor = new() { Content = Icons.Glyph("IconPlus", 16) };
    private readonly ComboBox _finish = Choice(Finishes.Select(f => f.Label));
    private readonly ComboBox _layer = Choice(Layers.Select(l => l.Label));
    private readonly ComboBox _scene = Choice(Scenes.Select(s => s.Label));
    private readonly TextBox _background = new();
    private readonly StackPanel _backgroundField;
    private readonly ComboBox _format = Choice(Formats.Select(f => f.Label));
    private readonly ComboBox _environment = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choisissez un environnement" };
    private readonly Slider _environmentRotation = new() { Minimum = 0, Maximum = 360, TickFrequency = 15, IsSnapToTickEnabled = true };
    private readonly StackPanel _environmentField = new() { Spacing = 12 };
    private readonly Button _final = new() { Content = Icons.Label("IconPlay", "Rendu HD"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Annuler le rendu HD", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private readonly Button _save = new() { Content = Icons.Label("IconSave", "Enregistrer l’image"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _work = ProductRenderer.WorkFolder();
    private readonly RenderPreferences _saved;

    private IReadOnlyList<RenderColor> _workshopColors = [];
    private List<RenderColor> _customColors;
    private string? _savedEnvironment;
    private string? _blender;
    private BlenderSession? _session;
    private bool _blenderSearched;
    private bool _syncingColor;
    private string? _modelPath;
    private string? _stlPath;
    private MeshModel? _mesh;
    private MeshModel? _loaded;
    private Quaternion _turn = Quaternion.Identity;
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
        _customColors = [.. saved.CustomColors ?? []];
        _hex.Text = PreviewColorParser.TryParse(saved.Hex, out var savedColor) ? saved.Hex : RenderPreferences.Default.Hex;
        _picker.Color = PreviewColorParser.TryParse(_hex.Text, out savedColor) ? savedColor : Colors.Red;
        _finish.SelectedIndex = Math.Clamp(saved.Finish, 0, Finishes.Length - 1);
        _layer.SelectedIndex = Math.Clamp(saved.Layer, 0, Layers.Length - 1);
        _scene.SelectedIndex = Math.Clamp(saved.Scene, 0, Scenes.Length - 1);
        _format.SelectedIndex = Math.Clamp(saved.Format, 0, Formats.Length - 1);
        _background.Text = saved.Background;
        _environmentRotation.Value = saved.EnvironmentRotation;
        _savedEnvironment = saved.Environment;

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
        _frame.Children.Add(new Border
        {
            Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12),
            IsHitTestVisible = false, Padding = new Thickness(12, 4),
            Child = new TextBlock { Text = "Glisser : tourner · Clic droit : déplacer · Molette : zoomer", Classes = { "caption" } }
        });
        _emptyCover = new Border { Background = (IBrush?)Application.Current?.FindResource("CanvasBrush"), Child = _emptyState };
        var stage = new Grid { ClipToBounds = true };
        stage.Children.Add(_frame);
        stage.Children.Add(_emptyCover);
        stage.SizeChanged += (_, _) => FitFrame();
        _stageHost = stage;
        var stageFrame = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = stage
        };
        var views = new WrapPanel();
        foreach (var (label, yaw, pitch) in Views)
        {
            var chip = new Button { Content = label, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
            chip.Click += (_, _) => SetView(yaw, pitch);
            views.Children.Add(chip);
        }
        var stageColumn = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 8 };
        stageColumn.Children.Add(views);
        Grid.SetRow(stageFrame, 1);
        stageColumn.Children.Add(stageFrame);
        Grid.SetRow(_progress, 2);
        stageColumn.Children.Add(_progress);
        Grid.SetRow(_status, 3);
        stageColumn.Children.Add(_status);

        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir un modèle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await BrowseAsync();
        // A part drawn with Y up arrives lying down: a quarter turn forward or sideways stands it up.
        var orientation = new WrapPanel();
        foreach (var (label, axis, tip) in new (string, Vector3?, string)[]
        {
            ("Basculer vers l’avant", Vector3.UnitX, "Quart de tour autour de l’axe X"),
            ("Basculer sur le côté", Vector3.UnitY, "Quart de tour autour de l’axe Y"),
            ("Orientation du fichier", null, "Revenir à la pièce telle qu’elle est dans le fichier")
        })
        {
            var button = new Button { Content = label, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
            ToolTip.SetTip(button, tip);
            button.Click += async (_, _) => await TurnAsync(axis);
            orientation.Children.Add(button);
        }
        _hex.LostFocus += (_, _) => HexEdited();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { HexEdited(); e.Handled = true; } };
        _picker.ColorChanged += (_, e) =>
        {
            if (_syncingColor) return;
            _hex.Text = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            ColorChanged();
        };
        ToolTip.SetTip(_picker, "Choisir une couleur personnalisée");
        AutomationProperties.SetName(_picker, "Choisir une couleur personnalisée");
        ToolTip.SetTip(_keepColor, "Ajouter cette couleur à mes couleurs");
        AutomationProperties.SetName(_keepColor, "Ajouter cette couleur à mes couleurs");
        _keepColor.Click += async (_, _) => await KeepColorAsync();
        _background.LostFocus += (_, _) => SettingsChanged();
        _background.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SettingsChanged(); e.Handled = true; } };
        _backgroundField = FormKit.Field("Couleur du fond", _background, hint: "Code #RRGGBB");
        foreach (var box in new[] { _finish, _layer, _environment }) box.SelectionChanged += (_, _) => SettingsChanged();
        _format.SelectionChanged += (_, _) => { FitFrame(); SettingsChanged(); };
        _scene.SelectionChanged += (_, _) => { ShowSceneFields(); SettingsChanged(); };
        _environmentRotation.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) SettingsChanged(); };
        var addEnvironment = new Button { Content = Icons.Label("IconOpen", "Ajouter un fichier HDR ou EXR"), HorizontalAlignment = HorizontalAlignment.Stretch };
        addEnvironment.Click += async (_, _) => await BrowseEnvironmentAsync();
        _environmentField.Children.Add(FormKit.Field("Environnement", _environment, hint: "Ceux fournis avec Blender, ou vos images HDRI (Poly Haven par exemple)."));
        _environmentField.Children.Add(addEnvironment);
        _environmentField.Children.Add(FormKit.Field("Rotation de l’environnement", _environmentRotation));
        ShowSceneFields();
        _final.Click += async (_, _) => await RenderFinalAsync();
        _cancel.Click += (_, _) => _finalRender?.Cancel();
        _save.Click += (_, _) => Save();

        var colorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        colorRow.Children.Add(_picker);
        Grid.SetColumn(_hex, 1);
        colorRow.Children.Add(_hex);
        Grid.SetColumn(_keepColor, 2);
        colorRow.Children.Add(_keepColor);
        var options = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.SectionLabel("Modèle"), open, _modelName, FormKit.Field("Orientation de la pièce", orientation),
                FormKit.SectionLabel("Filament"), _swatches, FormKit.Field("Couleur", colorRow, hint: "Nuancier ou code #RRGGBB. + l’ajoute à vos couleurs ; clic droit sur l’une d’elles pour la retirer."),
                FormKit.Field("Finition", _finish), FormKit.Field("Lignes de couche", _layer),
                FormKit.SectionLabel("Mise en scène"), FormKit.Field("Ambiance", _scene), _backgroundField, _environmentField, FormKit.Field("Format", _format)
            }
        };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(0, 4, 12, 4), Child = options }
        };
        var actions = new StackPanel { Spacing = 8, Children = { _final, _cancel, _save } };
        var panel = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
        panel.Children.Add(scroller);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 16 };
        root.Children.Add(stageColumn);
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Content = root;
        DetachedFromVisualTree += (_, _) => _session?.Dispose();
        ShowEmptyState();
        RebuildSwatches();
    }

    private readonly Grid _stageHost;

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
        AutomationProperties.SetName(viewport.Visual, "Cadrage du rendu : glisser pour tourner, clic droit pour déplacer, molette pour zoomer");
        if (PreviewColorParser.TryParse(_hex.Text, out var color)) viewport.ModelColor = color;
    }

    /// <summary>The graphics card view failed: the processor view takes over with the same model and camera.</summary>
    private void SwapViewport(IMeshViewport next)
    {
        var camera = _viewport.Camera;
        _frame.Children.Remove(_viewport.Visual);
        _viewport.CameraChanged -= CameraMoved;
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

    public async Task LoadModelAsync(string path)
    {
        ShowStatus("Préparation du modèle…", busy: true);
        MeshModel loaded;
        try { loaded = await Task.Run(() => MeshLoader.Load(path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            ShowStatus("Impossible d’ouvrir ce modèle : " + exception.Message);
            return;
        }
        _loaded = loaded;
        _turn = Quaternion.Identity;
        _modelPath = path;
        _modelName.Text = Path.GetFileName(path);
        ToolTip.SetTip(_modelName, path);
        _image.Source = null;
        var camera = _viewport.Camera;
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = (_saved.Yaw, _saved.Pitch, _saved.Zoom, 0, 0);
        await UseOrientationAsync();
    }

    /// <summary>Stands the part as asked (a quarter turn about X or Y, or back to the file's orientation) and renders it again.</summary>
    private async Task TurnAsync(Vector3? axis)
    {
        if (_loaded is null) return;
        _turn = axis is { } turnAxis ? Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(turnAxis, MathF.PI / 2))) : Quaternion.Identity;
        await UseOrientationAsync();
    }

    private async Task UseOrientationAsync()
    {
        if (_loaded is not { } loaded) return;
        var stl = Path.Combine(_work, $"model-{++_counter}.stl");
        var turn = _turn;
        MeshModel mesh;
        try { mesh = await Task.Run(() => ProductRenderer.PrepareModel(loaded, turn, stl)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Impossible de préparer le modèle : " + exception.Message);
            return;
        }
        if (_stlPath is { } previous) TryDelete(previous);
        _stlPath = stl;
        _mesh = mesh;
        _final.IsEnabled = _session is not null;
        _viewport.SetMesh(mesh);
        _viewport.RequestRender(false);
        ShowEmptyState();
        _generation++;
        await RunPreviewsAsync();
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

    private void SetView(double yaw, double pitch)
    {
        var camera = _viewport.Camera;
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = (yaw, pitch, 1, 0, 0);
        _viewport.RequestRender(false);
        CameraMoved();
    }

    /// <summary>The camera moves: the live render follows it (the 3D view underneath shows only until the first image).</summary>
    private void CameraMoved()
    {
        if (_mesh is null) return;
        // The studio has a floor: the camera stops at its level, in the 3D view as in the render.
        if (_viewport.Camera.Pitch < 0) { _viewport.Camera.Pitch = 0; _viewport.RequestRender(true); }
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
        if (!PreviewColorParser.TryParse(_hex.Text?.Trim(), out _)) { ShowStatus("Couleur du filament invalide : saisissez un code comme #D93A3A."); return null; }
        var scene = Scenes[_scene.SelectedIndex].Key;
        if (scene == "color" && !PreviewColorParser.TryParse(_background.Text?.Trim(), out _)) { ShowStatus("Couleur du fond invalide : saisissez un code comme #2F6FD6."); return null; }
        var environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? string.Empty;
        if (scene == "environment" && !File.Exists(environment)) { ShowStatus("Choisissez un environnement, ou ajoutez votre image HDR ou EXR."); return null; }
        var (_, ratioWidth, ratioHeight) = Formats[_format.SelectedIndex];
        // EEVEE (real time) for the live preview, Cycles on the graphics card for the final image.
        var (side, samples, engine) = pass == Pass.Live ? (960, 16, "eevee") : (2048, 256, "cycles");
        var (width, height) = ratioWidth >= ratioHeight ? (side, side * ratioHeight / ratioWidth) : (side * ratioWidth / ratioHeight, side);
        var (camera, rotation) = RenderCamera.From(mesh, _viewport.Camera, width, height);
        return new RenderSettings(_hex.Text!.Trim(), Finishes[_finish.SelectedIndex].Key, Layers[_layer.SelectedIndex].Height, scene, _background.Text?.Trim() ?? "#FFFFFF",
            rotation, 0, width, height, samples, environment, _environmentRotation.Value, camera, engine);
    }

    private void ShowSceneFields()
    {
        _backgroundField.IsVisible = Scenes[_scene.SelectedIndex].Key == "color";
        _environmentField.IsVisible = Scenes[_scene.SelectedIndex].Key == "environment";
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

    /// <summary>The user's own colours first, then the workshop filaments and the usual colours; a right click removes one of the user's.</summary>
    private void RebuildSwatches()
    {
        _swatches.Children.Clear();
        foreach (var color in _customColors) _swatches.Children.Add(Swatch(color, custom: true));
        foreach (var color in _workshopColors.Where(c => !_customColors.Any(mine => mine.Hex.Equals(c.Hex, StringComparison.OrdinalIgnoreCase) && mine.Finish == c.Finish)))
            _swatches.Children.Add(Swatch(color, custom: false));
        MarkSwatch();
    }

    private Button Swatch(RenderColor color, bool custom)
    {
        var swatch = new Button
        {
            Tag = color.Hex, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = Brushes.Transparent, BorderThickness = new Thickness(2),
            Content = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = new SolidColorBrush(Color.Parse(color.Hex)) }
        };
        ToolTip.SetTip(swatch, custom ? color.Label + " · ma couleur" : color.Label);
        AutomationProperties.SetName(swatch, "Couleur " + color.Label);
        swatch.Click += (_, _) =>
        {
            SetColor(color.Hex);
            _finish.SelectedIndex = Math.Max(0, Array.FindIndex(Finishes, f => f.Key == color.Finish));
        };
        if (custom)
        {
            var remove = new MenuItem { Header = "Retirer de mes couleurs" };
            remove.Click += async (_, _) => await RemoveColorAsync(color);
            swatch.ContextMenu = new ContextMenu { Items = { remove } };
        }
        return swatch;
    }

    private async Task KeepColorAsync()
    {
        HexEdited();
        if (!PreviewColorParser.TryParse(_hex.Text?.Trim(), out _) || TopLevel.GetTopLevel(this) is not Window owner) return;
        var finish = Finishes[_finish.SelectedIndex];
        var dialog = new TextPromptWindow("Ajouter à mes couleurs", $"{_hex.Text!.Trim().ToUpperInvariant()} · {finish.Label}. Elle restera proposée sur cette page.", string.Empty);
        if (!await dialog.ShowDialog<bool>(owner)) return;
        _customColors.RemoveAll(c => c.Label.Equals(dialog.Value, StringComparison.OrdinalIgnoreCase));
        _customColors.Insert(0, new RenderColor(dialog.Value, _hex.Text!.Trim().ToUpperInvariant(), finish.Key));
        SavePreferences();
        RebuildSwatches();
    }

    private async Task RemoveColorAsync(RenderColor color)
    {
        if (!await ConfirmRemovalWindow.AskAsync(this, color.Label, "La couleur ne sera plus proposée sur la page Rendu produit. Les images déjà enregistrées ne changent pas.")) return;
        _customColors.Remove(color);
        SavePreferences();
        RebuildSwatches();
    }

    private void HexEdited()
    {
        var text = _hex.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('#')) text = "#" + text;
        if (!PreviewColorParser.TryParse(text, out _)) { SettingsChanged(); return; }
        SetColor(text.ToUpperInvariant());
    }

    private void SetColor(string hex)
    {
        _hex.Text = hex;
        if (PreviewColorParser.TryParse(hex, out var color))
        {
            _syncingColor = true;
            _picker.Color = color;
            _syncingColor = false;
        }
        ColorChanged();
    }

    private void ColorChanged()
    {
        if (PreviewColorParser.TryParse(_hex.Text?.Trim(), out var color)) _viewport.ModelColor = color;
        MarkSwatch();
        SettingsChanged();
    }

    private void MarkSwatch()
    {
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        foreach (var swatch in _swatches.Children.OfType<Button>())
            swatch.BorderBrush = string.Equals(swatch.Tag as string, _hex.Text?.Trim(), StringComparison.OrdinalIgnoreCase) ? accent : Brushes.Transparent;
    }

    private void SavePreferences()
    {
        var camera = _viewport.Camera;
        _store.Save(new RenderPreferences(_customColors.ToList(), _hex.Text?.Trim() ?? RenderPreferences.Default.Hex, _finish.SelectedIndex,
            _layer.SelectedIndex, _scene.SelectedIndex, _background.Text?.Trim() ?? RenderPreferences.Default.Background, _format.SelectedIndex,
            (_environment.SelectedItem as RenderEnvironment)?.Path ?? _savedEnvironment, _environmentRotation.Value, camera.Yaw, camera.Pitch, camera.Zoom));
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

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

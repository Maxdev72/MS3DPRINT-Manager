using System.Diagnostics;
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
/// Product render page: one model, a few choices (colour, finish, layer lines, set, angle) and a Blender preview that
/// follows every change; the HD render is asked for explicitly. The choices and the user's own colours are kept between visits.
/// </summary>
public sealed class RenderStudioView : UserControl
{
    private static readonly (string Label, string Key)[] Finishes = [("Mat (PLA)", "matte"), ("Soie (PLA Silk)", "silk"), ("Brillant (PETG)", "glossy")];
    private static readonly (string Label, double Height)[] Layers = [("Lisse, sans lignes", 0), ("0,12 mm", 0.12), ("0,16 mm", 0.16), ("0,20 mm", 0.2), ("0,28 mm", 0.28)];
    private static readonly (string Label, string Key)[] Scenes = [("Studio clair", "light"), ("Studio sombre", "dark"), ("Fond de couleur", "color"), ("Fond transparent (PNG)", "transparent"), ("Environnement (HDRI)", "environment")];
    private static readonly (string Label, int Width, int Height)[] Formats = [("Carré 1:1", 1, 1), ("Paysage 4:3", 4, 3), ("Portrait 3:4", 3, 4), ("Large 16:9", 16, 9)];
    private static readonly (string Label, double Rotation, double Elevation)[] Views = [("Face", 0, 10), ("Trois-quarts", 35, 22), ("Dessus", 30, 65), ("Arrière", 180, 20)];
    private const int PreviewSide = 720, PreviewSamples = 24, FinalSide = 2048, FinalSamples = 256;

    private readonly Func<IReadOnlyList<RenderColor>> _loadColors;
    private readonly RenderPreferencesStore _store;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly StackPanel _emptyState = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
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
    private string? _savedEnvironment;
    private readonly Slider _rotation = new() { Minimum = 0, Maximum = 360, TickFrequency = 15, IsSnapToTickEnabled = true };
    private readonly Slider _elevation = new() { Minimum = 0, Maximum = 80, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Button _final = new() { Content = Icons.Label("IconPlay", "Rendu HD"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Annuler le rendu", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private readonly Button _save = new() { Content = Icons.Label("IconSave", "Enregistrer l’image"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _work = ProductRenderer.WorkFolder();

    private IReadOnlyList<RenderColor> _workshopColors = [];
    private List<RenderColor> _customColors;
    private string? _blender;
    private bool _blenderSearched;
    private bool _syncingColor;
    private string? _modelPath;
    private string? _stlPath;
    private string? _shown;
    private int _counter;
    private CancellationTokenSource? _render;

    /// <summary>Asks the window to file a copy of the image (temporary PNG, model path), like a capture of the 3D viewer.</summary>
    public event Action<string, string>? SaveRequested;

    public RenderStudioView(Func<IReadOnlyList<RenderColor>> loadColors, RenderPreferencesStore? store = null)
    {
        _loadColors = loadColors;
        _store = store ?? new RenderPreferencesStore();
        var saved = _store.Load();
        _customColors = [.. saved.CustomColors ?? []];
        _hex.Text = PreviewColorParser.TryParse(saved.Hex, out var savedColor) ? saved.Hex : RenderPreferences.Default.Hex;
        _picker.Color = PreviewColorParser.TryParse(_hex.Text, out savedColor) ? savedColor : Colors.Red;
        _finish.SelectedIndex = Math.Clamp(saved.Finish, 0, Finishes.Length - 1);
        _layer.SelectedIndex = Math.Clamp(saved.Layer, 0, Layers.Length - 1);
        _scene.SelectedIndex = Math.Clamp(saved.Scene, 0, Scenes.Length - 1);
        _format.SelectedIndex = Math.Clamp(saved.Format, 0, Formats.Length - 1);
        _background.Text = saved.Background;
        _rotation.Value = saved.Rotation;
        _elevation.Value = saved.Elevation;
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

        var stage = new Grid();
        stage.Children.Add(_image);
        stage.Children.Add(_emptyState);
        var stageFrame = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = stage
        };
        var stageColumn = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 8 };
        stageColumn.Children.Add(stageFrame);
        Grid.SetRow(_progress, 1);
        stageColumn.Children.Add(_progress);
        Grid.SetRow(_status, 2);
        stageColumn.Children.Add(_status);

        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir un modèle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await BrowseAsync();
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
        _background.LostFocus += (_, _) => ScheduleRender();
        _background.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ScheduleRender(); e.Handled = true; } };
        _backgroundField = FormKit.Field("Couleur du fond", _background, hint: "Code #RRGGBB");
        foreach (var box in new[] { _finish, _layer, _format, _environment }) box.SelectionChanged += (_, _) => ScheduleRender();
        _scene.SelectionChanged += (_, _) => { ShowSceneFields(); ScheduleRender(); };
        var addEnvironment = new Button { Content = Icons.Label("IconOpen", "Ajouter un fichier HDR ou EXR"), HorizontalAlignment = HorizontalAlignment.Stretch };
        addEnvironment.Click += async (_, _) => await BrowseEnvironmentAsync();
        _environmentField.Children.Add(FormKit.Field("Environnement", _environment, hint: "Ceux fournis avec Blender, ou vos images HDRI (Poly Haven par exemple)."));
        _environmentField.Children.Add(addEnvironment);
        _environmentField.Children.Add(FormKit.Field("Rotation de l’environnement", _environmentRotation));
        ShowSceneFields();
        foreach (var slider in new[] { _rotation, _elevation, _environmentRotation })
            slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) ScheduleRender(); };
        var views = new WrapPanel();
        foreach (var (label, rotation, elevation) in Views)
        {
            var chip = new Button { Content = label, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
            chip.Click += (_, _) => { _rotation.Value = rotation; _elevation.Value = elevation; };
            views.Children.Add(chip);
        }
        _delay.Tick += async (_, _) => { _delay.Stop(); await RenderAsync(final: false); };
        _final.Click += async (_, _) => await RenderAsync(final: true);
        _cancel.Click += (_, _) => _render?.Cancel();
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
                FormKit.SectionLabel("Modèle"), open, _modelName,
                FormKit.SectionLabel("Filament"), _swatches, FormKit.Field("Couleur", colorRow, hint: "Nuancier ou code #RRGGBB. + l’ajoute à vos couleurs ; clic droit sur l’une d’elles pour la retirer."),
                FormKit.Field("Finition", _finish), FormKit.Field("Lignes de couche", _layer),
                FormKit.SectionLabel("Mise en scène"), FormKit.Field("Ambiance", _scene), _backgroundField, _environmentField,
                FormKit.Field("Vue", views), FormKit.Field("Rotation du modèle", _rotation), FormKit.Field("Hauteur de vue", _elevation), FormKit.Field("Format", _format)
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
        ShowEmptyState();
        RebuildSwatches();
    }

    /// <summary>Called on each visit: finds Blender once and refreshes the filament colours of the workshop.</summary>
    public async Task ActivateAsync()
    {
        if (!_blenderSearched)
        {
            _blender = await Task.Run(BlenderLocator.Detect);
            _blenderSearched = true;
            _final.IsEnabled = _blender is not null && _stlPath is not null;
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
        _render?.Cancel();
        var stl = Path.Combine(_work, $"model-{++_counter}.stl");
        ShowStatus("Préparation du modèle…", busy: true);
        try { await Task.Run(() => ProductRenderer.PrepareModel(path, stl)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            ShowStatus("Impossible d’ouvrir ce modèle : " + exception.Message);
            return;
        }
        if (_stlPath is { } previous) TryDelete(previous);
        _modelPath = path;
        _stlPath = stl;
        _modelName.Text = Path.GetFileName(path);
        ToolTip.SetTip(_modelName, path);
        _final.IsEnabled = _blender is not null;
        await RenderAsync(final: false);
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
        if (!PreviewColorParser.TryParse(text, out _)) { ScheduleRender(); return; }
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
        MarkSwatch();
        ScheduleRender();
    }

    private void MarkSwatch()
    {
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        foreach (var swatch in _swatches.Children.OfType<Button>())
            swatch.BorderBrush = string.Equals(swatch.Tag as string, _hex.Text?.Trim(), StringComparison.OrdinalIgnoreCase) ? accent : Brushes.Transparent;
    }

    private void ScheduleRender()
    {
        if (_stlPath is null || _blender is null) return;
        _delay.Stop();
        _delay.Start();
    }

    private void SavePreferences() => _store.Save(new RenderPreferences(_customColors.ToList(), _hex.Text?.Trim() ?? RenderPreferences.Default.Hex, _finish.SelectedIndex,
        _layer.SelectedIndex, _scene.SelectedIndex, _background.Text?.Trim() ?? RenderPreferences.Default.Background, _rotation.Value, _elevation.Value, _format.SelectedIndex,
        (_environment.SelectedItem as RenderEnvironment)?.Path ?? _savedEnvironment, _environmentRotation.Value));

    private RenderSettings? ReadSettings(bool final)
    {
        if (!PreviewColorParser.TryParse(_hex.Text?.Trim(), out _)) { ShowStatus("Couleur du filament invalide : saisissez un code comme #D93A3A."); return null; }
        var scene = Scenes[_scene.SelectedIndex].Key;
        if (scene == "color" && !PreviewColorParser.TryParse(_background.Text?.Trim(), out _)) { ShowStatus("Couleur du fond invalide : saisissez un code comme #2F6FD6."); return null; }
        var environment = (_environment.SelectedItem as RenderEnvironment)?.Path ?? string.Empty;
        if (scene == "environment" && !File.Exists(environment)) { ShowStatus("Choisissez un environnement, ou ajoutez votre image HDR ou EXR."); return null; }
        var (_, ratioWidth, ratioHeight) = Formats[_format.SelectedIndex];
        var side = final ? FinalSide : PreviewSide;
        var (width, height) = ratioWidth >= ratioHeight ? (side, side * ratioHeight / ratioWidth) : (side * ratioWidth / ratioHeight, side);
        return new RenderSettings(_hex.Text!.Trim(), Finishes[_finish.SelectedIndex].Key, Layers[_layer.SelectedIndex].Height, scene, _background.Text?.Trim() ?? "#FFFFFF",
            _rotation.Value, _elevation.Value, width, height, final ? FinalSamples : PreviewSamples, environment, _environmentRotation.Value);
    }

    /// <summary>Starts a render and drops the one in progress: the latest choice always wins.</summary>
    private async Task RenderAsync(bool final)
    {
        _delay.Stop();
        if (_stlPath is not { } stl || _blender is not { } blender) return;
        if (ReadSettings(final) is not { } settings) return;
        SavePreferences();
        _render?.Cancel();
        using var render = _render = new CancellationTokenSource();
        var output = Path.Combine(_work, $"render-{++_counter}.png");
        var label = final ? $"Rendu HD {settings.Width} × {settings.Height} en cours" : "Aperçu en cours…";
        ShowStatus(final ? label + "…" : label, busy: true);
        _cancel.IsVisible = final;
        // Blender gives no progress in the background: the HD render shows the time spent instead.
        var watch = Stopwatch.StartNew();
        EventHandler tick = (_, _) => { if (ReferenceEquals(_render, render)) _status.Text = $"{label} · {watch.Elapsed:m\\:ss} écoulées (souvent 30 s à 2 min)."; };
        if (final) { _clock.Tick += tick; _clock.Start(); }
        try
        {
            await ProductRenderer.RenderAsync(blender, stl, settings, output, render.Token);
            Bitmap bitmap;
            await using (var stream = File.OpenRead(output)) bitmap = new Bitmap(stream);
            (_image.Source as IDisposable)?.Dispose();
            _image.Source = bitmap;
            if (_shown is { } previous) TryDelete(previous);
            _shown = output;
            _save.IsEnabled = true;
            ShowStatus(final ? $"Rendu HD prêt ({settings.Width} × {settings.Height}, {watch.Elapsed:m\\:ss}). Enregistrez-le dans le projet du modèle." : "Aperçu à jour. Lancez le rendu HD pour l’image finale.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(output);
            if (ReferenceEquals(_render, render)) ShowStatus("Rendu annulé.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (ReferenceEquals(_render, render)) ShowStatus(exception.Message);
        }
        finally
        {
            if (final) { _clock.Tick -= tick; _clock.Stop(); }
            if (ReferenceEquals(_render, render)) { _render = null; _cancel.IsVisible = false; _progress.IsVisible = false; }
        }
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
        _emptyState.IsVisible = _image.Source is null;
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
            return;
        }
        _emptyState.Children.Add(new TextBlock { Text = "Déposez un fichier STL, OBJ ou 3MF ici", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        _emptyState.Children.Add(new TextBlock { Text = "Choisissez la couleur et l’ambiance : l’aperçu se met à jour tout seul.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var browse = new Button { Content = Icons.Label("IconOpen", "Ouvrir un modèle"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        browse.Click += async (_, _) => await BrowseAsync();
        _emptyState.Children.Add(browse);
    }

    private void ShowStatus(string text, bool busy = false)
    {
        _status.Text = text;
        _progress.IsVisible = busy;
        _emptyState.IsVisible = _image.Source is null && !busy;
    }

    private static ComboBox Choice(IEnumerable<string> labels)
        => new() { ItemsSource = labels.ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

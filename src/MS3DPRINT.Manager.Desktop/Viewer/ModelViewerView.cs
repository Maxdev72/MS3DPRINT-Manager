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
using MS3DPRINT.Manager.Core.Slicing;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Native 3D viewer for STL and OBJ files, shown inside the main window, with a thin-wall advisory analysis.</summary>
public sealed partial class ModelViewerView : UserControl
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
    private readonly StackPanel _volumeRows = new() { Spacing = 6 };
    private readonly TextBlock _volumeNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private MeshMeasures? _measures;
    private readonly Button _compareButton = new() { Content = "Comparer avec une autre version…", IsEnabled = false };
    private readonly StackPanel _compareDetails = new() { Spacing = 8, IsVisible = false };
    private readonly CheckBox _showFirst = new() { IsChecked = true };
    private readonly CheckBox _showSecond = new() { IsChecked = true };
    private readonly CheckBox _alignCentres = new() { Content = "Aligner les centres" };
    private readonly StackPanel _compareTable = new() { Spacing = 2 };
    private readonly TextBlock _compareNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private MeshModel? _second;
    private MeshMeasures? _secondMeasures;
    private string? _secondName;
    private readonly ToggleButton _cutToggle = new() { IsEnabled = false };
    private readonly ToggleButton _faceToggle = new() { IsEnabled = false };
    private readonly ComboBox _cutAxis = new() { ItemsSource = new[] { "Horizontale (hauteur, axe Z)", "Verticale, axe X", "Verticale, axe Y" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _cutSlider = new() { Minimum = 0, Maximum = 100, Value = 50, IsEnabled = false };
    private readonly CheckBox _cutFlip = new() { Content = "Voir l’autre côté" };
    private readonly TextBlock _cutLabel = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Func<IReadOnlyList<string>>? _loadPrinterNames;
    private SlicerInstall? _slicer;
    private SlicerProfiles? _slicerProfiles;
    private IReadOnlyList<SlicerMachine> _machines = [];
    private readonly StackPanel _slicerPanel = new() { Spacing = 10, IsVisible = false };
    private readonly TextBlock _slicerNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _slicerMachine = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _slicerQuality = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _sliceButton = new() { Content = "Recalculer la durée", IsEnabled = false };
    private readonly TextBlock _sliceStatus = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _sliceDelay = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private CancellationTokenSource? _sliceCancel;
    private SliceResult? _slice;
    private SlicerMachine? _sliceMachineUsed;
    private readonly Button _createEstimate = new() { Content = "Créer une estimation avec ce modèle", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "primary-action" } };
    private readonly StackPanel _measureGroup = new() { Spacing = 10, IsVisible = false };
    private readonly StackPanel _cutGroup = new() { Spacing = 10, IsVisible = false };
    private readonly TextBlock _toolsHint = new() { Text = "Mesurer, couper ou comparer : choisissez un outil dans la barre au-dessus de la vue.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    private static readonly string[] TabNames = ["Infos", "Affichage", "Outils", "Analyse"];
    private Border? _stageFrame;
    private StackPanel[] _tabs = [];
    private Button[] _tabButtons = [];
    private string? _modelPath;
    private double? _estimatedMass;

    /// <summary>Raised when the user wants an estimate prefilled from the displayed model.</summary>
    public event Action<ModelEstimateSeed>? EstimateRequested;
    private readonly MeasureOverlay _overlay = new();
    private readonly ToggleButton _measureToggle = new() { IsEnabled = false };
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
    private readonly TextBox _scale = new() { Text = "1", PlaceholderText = "mm par unité du modèle" };
    private readonly ComboBox _units = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _unitsNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private bool _applyingUnit;
    private readonly Button _analyze = new() { Content = "Analyser les parois", IsEnabled = false };
    private readonly Button _cancelAnalysis = new() { Content = "Annuler", IsEnabled = false };
    private readonly Button _reset = new() { IsEnabled = false };
    private readonly ToggleButton _wireToggle = new();
    private readonly CheckBox _grid = new() { Content = "Grille au sol (G)", IsChecked = true };
    private readonly TextBlock _gridNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _captureTool = new() { IsEnabled = false };
    private readonly StackPanel _emptyState = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _activeLoad;
    private CancellationTokenSource? _activeAnalysis;
    private MeshModel? _mesh;
    private int _loadVersion;

    public ModelViewerView(Action close, Func<IReadOnlyList<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>>? loadFilaments = null, Func<IReadOnlyList<string>>? loadPrinterNames = null, Func<IReadOnlyList<ViewerPrinter>>? loadPrinters = null)
    {
        _loadFilaments = loadFilaments;
        _loadPrinterNames = loadPrinterNames;
        _loadPrinters = loadPrinters;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, View_DragOver);
        AddHandler(DragDrop.DropEvent, View_Drop);

        // viewport with its empty state and a navigation hint
        var hint = new TextBlock { Text = "Clic gauche : tourner · Clic droit : déplacer · Molette : zoom · R recentrer · W filaire · G grille · P image", Classes = { "caption" }, Margin = new Thickness(14, 0, 14, 10), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        _emptyState.Children.Add(new TextBlock { Text = "Déposez un fichier STL, OBJ ou 3MF ici", FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
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
        _stageFrame = stage;
        _stage.Children.Add(_emptyState);
        _stage.Children.Add(hint);
        UseViewport(CreateGpuViewport() ?? new MeshViewport());
        _renderer.SelectionChanged += (_, _) => UseViewport(_renderer.SelectedIndex == 0 ? CreateGpuViewport() ?? new MeshViewport() : new MeshViewport());

        // floating toolbar over the 3D view: one icon per tool, each with its shortcut
        static void Tool(Button button, string icon, string tip)
        {
            button.Content = Icons.Glyph(icon, 18);
            button.Classes.Add("tool");
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);
        }
        Tool(_reset, "IconRecenter", "Recentrer la vue (R)");
        Tool(_wireToggle, "IconWireframe", "Affichage filaire (W)");
        Tool(_measureToggle, "IconMeasure", "Mesurer des distances (M)");
        Tool(_cutToggle, "IconCut", "Plan de coupe (C)");
        Tool(_faceToggle, "IconFace", "Poser sur une face (F)");
        Tool(_captureTool, "IconCamera", "Enregistrer une image de la vue (P)");
        var compareTool = new Button { IsEnabled = false };
        Tool(compareTool, "IconCompare", "Comparer avec une autre version");
        _compareButton.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) compareTool.IsEnabled = _compareButton.IsEnabled; };
        compareTool.Click += async (_, _) => { ShowTab(2); await CompareAsync(); };
        _reset.Click += (_, _) => _viewport.ResetView();
        _captureTool.Click += async (_, _) => await CaptureViewAsync();
        _wireToggle.IsCheckedChanged += (_, _) => _mode.SelectedIndex = _wireToggle.IsChecked == true ? 1 : 0;
        var divider = new Border { Width = 1, Margin = new Thickness(4, 6), Background = (IBrush?)Application.Current?.FindResource("BorderBrush") };
        _stage.Children.Add(new Border
        {
            Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _reset, _wireToggle, divider, _measureToggle, _cutToggle, _faceToggle, compareTool, _captureTool } }
        });

        // side panel: back and open on top, four tabs, the estimate action always in reach
        var back = new Button { Content = Icons.Label("IconArrowLeft", "Retour"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => close();
        var browse = new Button { Content = Icons.Label("IconOpen", "Ouvrir un fichier") };
        browse.Click += async (_, _) => await BrowseAsync();
        var topBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        topBar.Children.Add(back);
        Grid.SetColumn(browse, 1);
        topBar.Children.Add(browse);

        _grid.IsCheckedChanged += (_, _) => ApplyGrid();
        _mode.SelectionChanged += (_, _) => { _viewport.Wireframe = _mode.SelectedIndex == 1; _wireToggle.IsChecked = _mode.SelectedIndex == 1; UpdateModeMessage(); _viewport.RequestRender(false); };
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
        _compareButton.Click += async (_, _) => await CompareAsync();
        _showFirst.IsCheckedChanged += (_, _) => ApplyComparison();
        _showSecond.IsCheckedChanged += (_, _) => ApplyComparison();
        _alignCentres.IsCheckedChanged += (_, _) => ApplyComparison();
        _cutToggle.IsCheckedChanged += (_, _) => { if (_cutToggle.IsChecked == true) ShowTab(2); UpdateCut(); UpdateToolPanels(); };
        _cutAxis.SelectionChanged += (_, _) => UpdateCut();
        _cutSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) UpdateCut(); };
        _cutFlip.IsCheckedChanged += (_, _) => UpdateCut();
        _measureToggle.IsCheckedChanged += (_, _) => { if (_measureToggle.IsChecked == true) { _faceToggle.IsChecked = false; ShowTab(2); } UpdateMeasureMode(); };
        _faceToggle.IsCheckedChanged += (_, _) => { if (_faceToggle.IsChecked == true) { _measureToggle.IsChecked = false; ShowTab(2); } UpdateFaceMode(); };
        _clearMeasures.Click += (_, _) => ClearMeasures();
        _scale.IsVisible = false;
        _units.ItemsSource = ModelUnits.All.Select(unit => unit.Label).Append("Personnalisé…").ToList();
        _units.SelectedIndex = 0;
        _units.SelectionChanged += (_, _) => UnitChosen();
        _scale.TextChanged += (_, _) => { RefreshMeasures(); RefreshDimensions(); RebuildComparison(); ApplyPlate(); };
        _slicerPanel.Children.Add(_slicerNote);
        _slicerPanel.Children.Add(Muted("Imprimante"));
        _slicerPanel.Children.Add(_slicerMachine);
        _slicerPanel.Children.Add(Muted("Qualité"));
        _slicerPanel.Children.Add(_slicerQuality);
        _slicerPanel.Children.Add(_sliceButton);
        _slicerPanel.Children.Add(_sliceStatus);
        _slicerMachine.SelectionChanged += (_, _) => { FillQualities(); ScheduleSlice(); };
        _slicerQuality.SelectionChanged += (_, _) => { ScheduleSlice(); ScheduleAssessment(); };
        _sliceButton.Click += async (_, _) => await RunSliceAsync();
        _sliceDelay.Tick += async (_, _) => { _sliceDelay.Stop(); await RunSliceAsync(); };
        _createEstimate.Click += (_, _) =>
        {
            if (_modelPath is null) return;
            var choice = _material.SelectedItem as MaterialChoice;
            EstimateRequested?.Invoke(new ModelEstimateSeed(Path.GetFileNameWithoutExtension(_modelPath), _estimatedMass, choice?.FilamentKey, _modelPath, _slice?.TimeHours ?? _quick?.TimeSeconds / 3600, _slice is null ? null : _sliceMachineUsed?.PrinterModel));
        };
        _material.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MaterialChoice>((choice, _) => new TextBlock { Text = choice?.Label ?? string.Empty });
        _material.ItemsSource = MeshVolume.Presets.ToList();
        _material.SelectedIndex = 0;
        _material.SelectionChanged += (_, _) => { RefreshVolume(); ScheduleSlice(); ScheduleAssessment(); };
        _infill.TextChanged += (_, _) => { RefreshVolume(); ScheduleSlice(); ScheduleAssessment(); };
        _walls.TextChanged += (_, _) => { RefreshVolume(); ScheduleSlice(); ScheduleAssessment(); };
        _scale.TextChanged += (_, _) => { RefreshVolume(); ScheduleSlice(); ScheduleAssessment(); if (!_applyingUnit) SaveCustomScale(); };
        _speedClass.ItemsSource = ModelAssessment.SpeedClasses.Select(speed => speed.Label).ToList();
        _speedClass.SelectedIndex = 0;
        InitializeAssessment();
        InitializeOrientation();
        InitializePlate();
        RefreshVolume();
        _hex.LostFocus += (_, _) => ApplyColor();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyColor(); e.Handled = true; } };
        UpdateSwatches();

        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _cancelAnalysis.Click += (_, _) => _activeAnalysis?.Cancel();
        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 4 };
        void Cell(Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); limits.Children.Add(control); }
        Cell(Muted("Seuil de paroi"), 0, 0);
        Cell(_threshold, 1, 0);
        Cell(Unit("mm"), 1, 1);
        var analysisButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        analysisButtons.Children.Add(_analyze);
        Grid.SetColumn(_cancelAnalysis, 1);
        analysisButtons.Children.Add(_cancelAnalysis);
        var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        hexRow.Children.Add(Muted("Hex"));
        Grid.SetColumn(_hex, 1);
        hexRow.Children.Add(_hex);

        _measureGroup.Children.Add(FormKit.SectionLabel("Mesures"));
        _measureGroup.Children.Add(new TextBlock { Text = "Cliquez deux points du modèle : ils s’accrochent aux sommets proches. Les longueurs sont en mm selon les unités du modèle. Échap annule le point en cours.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        _measureGroup.Children.Add(_measureList);
        _measureGroup.Children.Add(_clearMeasures);
        _cutGroup.Children.Add(FormKit.SectionLabel("Plan de coupe"));
        _cutGroup.Children.Add(_cutAxis);
        _cutGroup.Children.Add(_cutSlider);
        _cutGroup.Children.Add(_cutFlip);
        _cutGroup.Children.Add(_cutLabel);
        var compareGroup = new StackPanel { Spacing = 10, Children = { FormKit.SectionLabel("Comparer deux versions"), _compareButton, _compareDetails } };

        var infoTab = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _title, _location, _dimensions, _message, _feasibilityChip,
                Muted("Unités du modèle"), _units, _scale, _unitsNote,
                FormKit.SectionLabel("Résultats"), _volumeResult, _volumeRows, Muted("Matière"), _material,
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 8, Children = { _infill, Place(Unit("% remplissage"), 1), Place(_walls, 2), Place(Unit("mm parois"), 3) } },
                _supports, Muted("Vitesse de la machine"), _speedClass,
                Muted("Plateau d’impression"), _plateChoice, _plateText, _plateStatus, _volumeNote,
                FormKit.SectionLabel("Durée d’impression"), _slicerPanel
            }
        };
        var displayTab = new StackPanel { Spacing = 10, Children = { Muted("Mode d’affichage"), _mode, Muted("Couleur du modèle"), _swatches, hexRow, Muted("Repère"), _grid, _gridNote, Muted("Rendu"), _renderer, _rendererNote } };
        var toolsTab = new StackPanel { Spacing = 14, Children = { _orientGroup, _toolsHint, _measureGroup, _cutGroup, compareGroup } };
        var analysisTab = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Repère les parois plus fines que votre seuil. Choisissez-le selon votre buse, votre matériau et votre trancheur ; la mesure suit les unités du modèle.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                limits, analysisButtons, _analysisSummary, _analysis
            }
        };
        var feasibility = FeasibilityControls();
        for (var i = 0; i < feasibility.Length; i++) analysisTab.Children.Insert(i, feasibility[i]);
        _tabs = [infoTab, displayTab, toolsTab, analysisTab];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _tabButtons = TabNames.Select((name, index) =>
        {
            var chip = new Button { Content = name, Classes = { "filter-chip" } };
            chip.Click += (_, _) => ShowTab(index);
            tabRow.Children.Add(chip);
            return chip;
        }).ToArray();

        var tabBody = new Panel();
        foreach (var tab in _tabs) tabBody.Children.Add(tab);
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(0, 4, 10, 4), Child = tabBody }
        };
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        panel.Children.Add(topBar);
        Grid.SetRow(tabRow, 1);
        panel.Children.Add(tabRow);
        Grid.SetRow(scroller, 2);
        panel.Children.Add(scroller);
        Grid.SetRow(_createEstimate, 3);
        panel.Children.Add(_createEstimate);
        ShowTab(0);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        root.Children.Add(stage);
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Content = root;
    }

    /// <summary>Frames the 3D view in the accent colour while it has the focus from the keyboard, so the shortcuts are discoverable.</summary>
    private void SetStageFocus(bool focused)
    {
        if (_stageFrame is null) return;
        _stageFrame.BorderBrush = (IBrush?)Application.Current?.FindResource(focused ? "FocusBorderBrush" : "SubtleBorderBrush");
    }

    /// <summary>Shows one tab of the side panel (0 info, 1 display, 2 tools, 3 analysis).</summary>
    private void ShowTab(int index)
    {
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabs[i].IsVisible = i == index;
            _tabButtons[i].Classes.Set("selected", i == index);
            AutomationProperties.SetName(_tabButtons[i], TabNames[i] + (i == index ? ", onglet sélectionné" : ", onglet"));
        }
    }

    /// <summary>Shows the settings of the tools that are in use; a hint while none is.</summary>
    private void UpdateToolPanels()
    {
        _measureGroup.IsVisible = _measureToggle.IsChecked == true || _segments.Count > 0 || _pendingPoint is not null;
        _cutGroup.IsVisible = _cutToggle.IsChecked == true;
        _toolsHint.IsVisible = !_measureGroup.IsVisible && !_cutGroup.IsVisible && _second is null;
    }

    /// <summary>Raised with a PNG written to a temporary file and the model path, when the user saves a picture of the view.</summary>
    public event Action<string, string>? ImageCaptured;

    private async Task CaptureViewAsync()
    {
        if (_modelPath is null) return;
        var image = await _viewport.CaptureAsync();
        if (image is null) { _message.Text = "Impossible de capturer la vue."; return; }
        var temporary = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(_modelPath)}_{DateTime.Now:yyyyMMdd-HHmmss}.png");
        try
        {
            await Task.Run(() =>
            {
                using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Opaque);
                using (var buffer = bitmap.Lock())
                    for (var row = 0; row < image.Height; row++)
                        System.Runtime.InteropServices.Marshal.Copy(image.Rgba, row * image.Width * 4, buffer.Address + row * buffer.RowBytes, image.Width * 4);
                bitmap.Save(temporary, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            });
            ImageCaptured?.Invoke(temporary, _modelPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _message.Text = "Impossible d’enregistrer l’image : " + exception.Message;
        }
    }

    /// <summary>Raised with the file name once a model is displayed.</summary>
    public event Action<string>? ModelLoaded;
    public event Action<string>? FileOpened;

    /// <summary>Stops running loads and analyses when the viewer is left.</summary>
    public void Release()
    {
        _loadVersion++;
        _activeLoad?.Cancel();
        _activeAnalysis?.Cancel();
        _assessDelay.Stop();
        _assessCancel?.Cancel();
        _autoOrientCancel?.Cancel();
        _gpuWatch?.Stop();
        if (_viewport is MeshViewport software) software.Release();
    }

    public void Resume()
    {
        if (_viewport is MeshViewport software) software.Resume();
        else if (_viewport is GpuViewportHost { IsReady: false }) _gpuWatch?.Start();
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
        next.WindingInverted = _measures?.Inverted == true;
        ApplyGrid(next);
        ApplyComparison(next);
        if (PreviewColorParser.TryParse(_hex.Text, out var color)) next.ModelColor = color;
        AutomationProperties.SetName(next.Visual, "Vue 3D du modèle : souris pour tourner, déplacer et zoomer, touche R pour recentrer, W pour le mode filaire");
        if (_stage.Children.Contains(_overlay)) _stage.Children.Remove(_overlay);
        _overlay.Camera = next.Camera;
        _overlay.Mesh = _mesh;
        _stage.Children.Insert(1, _overlay);
        next.CameraChanged += () => _overlay.InvalidateVisual();
        next.Clicked += point => { if (_faceToggle.IsChecked == true) _ = FaceClickAsync(point); else _ = MeasureClickAsync(point); };
        next.Visual.Cursor = _measureToggle.IsChecked == true || _faceToggle.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
        next.Visual.KeyDown += Viewport_KeyDown;
        // The frame marks keyboard focus only (Tab, arrows): a click or the automatic focus after loading must not draw it.
        next.Visual.GotFocus += (_, e) => SetStageFocus(e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional);
        next.Visual.LostFocus += (_, _) => SetStageFocus(false);
        next.Visual.PointerPressed += (_, _) => { SetStageFocus(false); next.Visual.Focus(); };
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
        else if (e.Key == Key.P && _captureTool.IsEnabled) { _ = CaptureViewAsync(); e.Handled = true; }
        else if (e.Key == Key.C && _cutToggle.IsEnabled) { _cutToggle.IsChecked = _cutToggle.IsChecked != true; e.Handled = true; }
        else if (e.Key == Key.Escape && _pendingPoint is not null) { _pendingPoint = null; RefreshMeasures(); e.Handled = true; }
        else if (e.Key == Key.G && _grid.IsEnabled) { _grid.IsChecked = _grid.IsChecked != true; e.Handled = true; }
        else if (e.Key == Key.F && _faceToggle.IsEnabled) { _faceToggle.IsChecked = _faceToggle.IsChecked != true; e.Handled = true; }
        else if (e.Key == Key.Escape && _faceToggle.IsChecked == true) { _faceToggle.IsChecked = false; e.Handled = true; }
        else if (e.Key == Key.O && _original is not null) { _ = AutoOrientAsync(); e.Handled = true; }
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
            FileTypeFilter = [new FilePickerFileType("Modèles 3D (STL, OBJ, 3MF)") { Patterns = ["*.stl", "*.obj", "*.3mf"] }]
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
        _autoOrientCancel?.Cancel();
        _activeLoad?.Cancel();
        _analyze.IsEnabled = false;
        _analysisSummary.IsVisible = false;
        _analysis.Text = "Analyse indicative sur échantillons, sans certification d’imprimabilité. Aucun fichier n’est modifié.";
        using var load = new CancellationTokenSource();
        _activeLoad = load;
        _message.Text = "Chargement du modèle…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var loaded = await Task.Run(() => MeshLoader.Load(path, load.Token), load.Token);
            if (version != _loadVersion) return;
            // the model is stood on the build plate in the orientation it was saved in: centred, lowest point at Z = 0
            _original = loaded;
            _orientation.Reset();
            SyncAngles();
            var mesh = _orientation.Apply(loaded);
            _mesh = mesh;
            _overlay.Mesh = mesh;
            _measureToggle.IsEnabled = true;
            _cutToggle.IsEnabled = true;
            _orientGroup.IsEnabled = true;
            _faceToggle.IsEnabled = true;
            _captureTool.IsEnabled = true;
            _compareButton.IsEnabled = true;
            ClearComparison();
            _cutToggle.IsChecked = false;
            // development hook, like the other MS3DPRINT_START_* variables: open with a cut at this percentage
            if (double.TryParse(Environment.GetEnvironmentVariable("MS3DPRINT_START_CUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out var startCut))
            {
                _cutSlider.Value = startCut;
                _cutToggle.IsChecked = true;
            }
            UpdateCut();
            ClearMeasures();
            _ = ComputeMeasuresAsync(mesh, version);
            _slice = null;
            ScheduleSlice();
            ClearAssessment();
            ScheduleAssessment();
            _viewport.SetMesh(mesh);
            _emptyState.IsVisible = false;
            _analyze.IsEnabled = _activeAnalysis is null;
            _reset.IsEnabled = true;
            _title.Text = Path.GetFileName(path);
            _location.Text = path;
            _modelPath = path;
            ToolTip.SetTip(_location, path);
            _location.IsVisible = _dimensions.IsVisible = true;
            ApplyRememberedUnit(path, mesh);
            PushPlate(_viewport);
            // development hook, like the other MS3DPRINT_START_* variables: open with a second version already loaded
            if (Environment.GetEnvironmentVariable("MS3DPRINT_START_COMPARE") is { Length: > 0 } startCompare) _ = LoadSecondAsync(startCompare);
            _message.Text = $"{mesh.TriangleCount:N0} triangles · chargé en {stopwatch.Elapsed.TotalSeconds:N1} s.";
            // development hook, like the other MS3DPRINT_START_* variables: open turned a quarter turn about X by this many degrees
            if (double.TryParse(Environment.GetEnvironmentVariable("MS3DPRINT_START_ROTATE_X"), NumberStyles.Float, CultureInfo.InvariantCulture, out var startRotateX))
            {
                _orientation.RotateAxis(0, startRotateX);
                OrientationChanged();
            }
            // development hook, like the other MS3DPRINT_START_* variables: open with the ground grid shown
            if (Environment.GetEnvironmentVariable("MS3DPRINT_START_GRID") == "1") _grid.IsChecked = true;
            // development hook, like the other MS3DPRINT_START_* variables: open with a plate of these dimensions, e.g. "50x50x50"
            if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PLATE") is { Length: > 0 } startPlate && _plateChoice.ItemsSource is IEnumerable<PlateChoice> plateChoices)
            {
                _plateChoice.SelectedItem = plateChoices.Last();
                _plateText.Text = startPlate;
            }
            // development hook, like the other MS3DPRINT_START_* variables: open with this zoom (1 frames the whole model)
            if (double.TryParse(Environment.GetEnvironmentVariable("MS3DPRINT_START_ZOOM"), NumberStyles.Float, CultureInfo.InvariantCulture, out var startZoom) && startZoom > 0)
            {
                _viewport.Camera.Zoom = startZoom;
                _viewport.RequestRender(false);
            }
            // development hook, like the other MS3DPRINT_START_* variables: open on this tab of the side panel (0 to 3)
            if (int.TryParse(Environment.GetEnvironmentVariable("MS3DPRINT_START_TAB"), out var startTab) && startTab >= 0 && startTab < _tabs.Length) ShowTab(startTab);
            ModelLoaded?.Invoke(Path.GetFileName(path));
            FileOpened?.Invoke(path);
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
        ReloadPlates();
        await EnsureSlicerAsync();
        if (_loadFilaments is null) return;
        IReadOnlyList<MS3DPRINT.Manager.Core.Filaments.FilamentProfile> filaments;
        try { filaments = await Task.Run(_loadFilaments); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { return; }
        var selected = (_material.SelectedItem as MaterialChoice)?.Label;
        var choices = new List<MaterialChoice>(MeshVolume.Presets);
        choices.AddRange(filaments.Select(f => new MaterialChoice($"{f.Brand} {f.Name} ({f.Material}, {f.PricePerKg:0.##} €/kg)", MeshVolume.DensityFor(f.Material), f.PricePerKg, $"{f.Brand} {f.Name}", MaxFlowMm3S: MeshVolume.MaxFlowFor(f.Material))));
        _material.ItemsSource = choices;
        _material.SelectedItem = choices.FirstOrDefault(c => c.Label == selected) ?? choices[0];
    }

    /// <summary>Finds Bambu Studio or OrcaSlicer once and fills the printer list, preferring the printers of the workshop.</summary>
    private async Task EnsureSlicerAsync()
    {
        if (_slicerProfiles is not null) return;
        var preferred = _loadPrinterNames is null ? [] : await Task.Run(() => { try { return _loadPrinterNames(); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return []; } });
        var found = await Task.Run(() =>
        {
            var install = SlicerLocator.Detect();
            return install is null ? default : (Install: install, Profiles: new SlicerProfiles(install), Machines: new SlicerProfiles(install).Machines());
        });
        _slicerPanel.IsVisible = true;
        if (found.Install is null)
        {
            _slicerNote.Text = "Aucun découpeur détecté. Installez Bambu Studio ou OrcaSlicer pour obtenir la durée d’impression réelle sans quitter le logiciel.";
            _slicerMachine.IsVisible = _slicerQuality.IsVisible = _sliceButton.IsVisible = false;
            return;
        }
        _slicer = found.Install;
        _slicerProfiles = found.Profiles;
        _machines = found.Machines.Where(machine => machine.CanBeSlicedAutomatically).ToList();
        _slicerNote.Text = _slicer.Name + " calcule la durée et le filament sans afficher sa fenêtre.";
        _slicerMachine.ItemsSource = _machines.Select(machine => machine.Name).ToList();
        var firstPrinter = preferred.FirstOrDefault(name => SlicerProfiles.MachineFor(found.Machines, name) is not null);
        var first = firstPrinter is null ? _machines.FirstOrDefault() : SlicerProfiles.MachineFor(found.Machines, firstPrinter);
        // a double-nozzle printer cannot be sliced from the command line: say which single-nozzle machine stands in for it
        if (firstPrinter is not null && first is not null && !string.Equals(first.PrinterModel, firstPrinter, StringComparison.OrdinalIgnoreCase))
            _slicerNote.Text += $" {firstPrinter} est découpée comme {first.PrinterModel} (une seule buse) : la durée est une très bonne approximation.";
        _slicerMachine.SelectedItem = first?.Name;
        FillQualities();
        ScheduleSlice();
    }

    private SlicerMachine? CurrentMachine => _machines.FirstOrDefault(machine => machine.Name == _slicerMachine.SelectedItem as string);

    private void FillQualities()
    {
        if (_slicerProfiles is null || CurrentMachine is not { } machine) return;
        var processes = _slicerProfiles.Processes(machine);
        _slicerQuality.ItemsSource = processes;
        _slicerQuality.SelectedItem = processes.FirstOrDefault(name => name == machine.DefaultProcess) ?? processes.FirstOrDefault();
    }

    /// <summary>Waits a moment after the last change, then slices again; a new change cancels a running slice.</summary>
    private void ScheduleSlice()
    {
        if (_slicerProfiles is null || _mesh is null) return;
        _sliceDelay.Stop();
        _sliceDelay.Start();
    }

    private async Task RunSliceAsync()
    {
        if (_slicer is null || _slicerProfiles is null || _modelPath is null || CurrentMachine is not { } machine || _slicerQuality.SelectedItem is not string quality) return;
        _sliceCancel?.Cancel();
        var cancellation = _sliceCancel = new CancellationTokenSource();
        var material = _material.SelectedItem as MaterialChoice;
        int? infill = double.TryParse(_infill.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var infillValue) && infillValue is >= 0 and <= 100 ? (int)Math.Round(infillValue) : null;
        int? loops = double.TryParse(_walls.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var wallMm) && wallMm > 0 ? Math.Max(1, (int)Math.Round(wallMm / 0.4)) : null;
        var filament = _slicerProfiles.FilamentFor(machine, material?.MaterialName);
        var path = _modelPath;
        string? orientedCopy = null;
        _sliceButton.IsEnabled = false;
        _slice = null;
        RefreshVolume();
        _sliceStatus.Text = "Découpe en cours avec " + _slicer.Name + "…";
        try
        {
            // a turned model is sliced from a temporary copy in millimetres; an untouched one from its own file
            if (!_orientation.IsIdentity && _mesh is { } shown) orientedCopy = await WriteOrientedCopyAsync(shown);
            var request = new SliceRequest(orientedCopy ?? path, machine, quality, filament, infill, loops,
                orientedCopy is null ? MeasureScale : 1, _supports.IsChecked == true);
            var result = await new SliceRunner(_slicer).RunAsync(_slicerProfiles, request, cancellation.Token);
            if (cancellation.IsCancellationRequested || path != _modelPath) return;
            _slice = result;
            _sliceMachineUsed = machine;
            _sliceStatus.Text = $"{machine.Name} · {quality} · couche {result.LayerHeight:0.##} mm · {result.Layers} couches · {result.InfillPercent:0.#} % de remplissage";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or FileNotFoundException or UnauthorizedAccessException)
        {
            if (!cancellation.IsCancellationRequested)
                _sliceStatus.Text = exception.Message.Contains("multi-extruder", StringComparison.OrdinalIgnoreCase)
                    ? "Cette imprimante a plusieurs buses : le découpage automatique n’en prend pas en charge. Choisissez le profil d’une imprimante à une buse."
                    : exception.Message;
        }
        finally
        {
            if (orientedCopy is not null)
                try { File.Delete(orientedCopy); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (ReferenceEquals(_sliceCancel, cancellation)) _sliceButton.IsEnabled = true;
        }
        RefreshVolume();
    }

    private static string FormatDuration(double hours)
    {
        var total = TimeSpan.FromHours(hours);
        return total.TotalHours >= 1 ? $"{(int)total.TotalHours} h {total.Minutes:00} min" : $"{Math.Max(1, total.Minutes)} min";
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
            if (_second is not null) RebuildComparison();
            _viewport.WindingInverted = measures.Inverted;
            _viewport.RequestRender(false);
        }
        catch (OperationCanceledException) { return; }
        RefreshVolume();
    }

    private void RefreshVolume()
    {
        _estimatedMass = null;
        _createEstimate.IsEnabled = false;
        _volumeRows.Children.Clear();
        _volumeResult.IsVisible = true;
        if (_mesh is null)
        {
            _volumeResult.Text = "Ouvrez un modèle pour estimer son volume, son poids et sa durée d’impression.";
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
        // what the user came for first (time, weight, cost), then the geometry
        var main = new List<(string Label, string Value)>();
        var details = new List<(string Label, string Value)>();
        string message = string.Empty;
        if (_material.SelectedItem is MaterialChoice material
            && double.TryParse(_infill.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var infill)
            && double.TryParse(_walls.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var walls) && infill >= 0 && walls >= 0)
        {
            var mass = MeshVolume.EstimateMass(volumeCm3, areaCm2, material.Density, infill, walls);
            var solid = volumeCm3 * material.Density;
            if (_slice is { } sliced)
            {
                mass = sliced.WeightGrams(material.Density);
                main.Add(("Durée d’impression", FormatDuration(sliced.TimeHours)));
                main.Add(("Poids", mass.ToString("0.#", french) + " g"));
                details.Add(("Filament", (sliced.FilamentLengthMm / 1000).ToString("0.##", french) + " m"));
            }
            else if (_quick is { } quick)
            {
                // the weight follows the material at once; the rest is recomputed a moment after each change
                mass = quick.WeightGrams / _quickDensity * material.Density;
                main.Add(("Durée estimée", FormatDuration(quick.TimeSeconds / 3600)));
                main.Add(("Poids estimé", mass.ToString("0.#", french) + " g"));
                details.Add(("Filament", (quick.FilamentLengthMm / 1000).ToString("0.##", french) + " m"));
                if (quick.SupportVolumeMm3 > 0) details.Add(("Supports", (quick.SupportVolumeMm3 / 1000).ToString("0.##", french) + " cm³ de matière"));
            }
            else main.Add(("Poids estimé", $"{mass.ToString("0.#", french)} g à {infill:0.##} %"));
            if (material.PricePerKg is { } price) main.Add(("Coût matière", $"{(decimal)mass * price / 1000m:0.00} €"));
            _estimatedMass = mass;
            _createEstimate.IsEnabled = _modelPath is not null;
            details.Add(("Poids plein", solid.ToString("0.#", french) + " g"));
        }
        else message = "Saisissez un remplissage et une épaisseur de parois valides pour estimer le poids.";
        details.Insert(0, ("Volume", volumeCm3.ToString("0.##", french) + " cm³"));
        details.Insert(1, ("Surface", areaCm2.ToString("0.##", french) + " cm²"));

        void Row(string label, string value, bool strong)
        {
            var labelText = new TextBlock { Text = label, Classes = { "muted" } };
            var valueText = new TextBlock { Text = value, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, FontSize = strong ? 15 : 13, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(labelText);
            row.Children.Add(valueText);
            _volumeRows.Children.Add(row);
        }
        foreach (var (label, value) in main) Row(label, value, strong: true);
        if (main.Count > 0) _volumeRows.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4), Background = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush") });
        foreach (var (label, value) in details) Row(label, value, strong: false);
        _volumeResult.IsVisible = message.Length > 0;
        _volumeResult.Text = message;
        _volumeNote.Text = (measures.IsClosed switch
        {
            false => "Maillage ouvert : le volume n’est qu’approximatif, corrigez le maillage avant de vous y fier. ",
            null => "Fermeture du maillage non vérifiée (modèle très volumineux). ",
            _ => string.Empty
        }) + (_slice is not null ? "Durée et poids calculés par le découpeur."
            : _quick is not null ? "Estimation rapide du logiciel, sans découpeur : poids en général à ±5 %. La durée est une moyenne de plusieurs imprimantes : elle peut s’écarter d’environ 25 % selon la vitesse réelle de votre machine (réglage ci-dessus). Le découpeur donnera les valeurs exactes."
            : "Estimation indicative : le découpeur donnera le poids et la durée exacts.");
    }

    /// <summary>Applies the cutting plane to the 3D view and says where it cuts, in millimetres from the base of the model.</summary>
    private void UpdateCut()
    {
        var on = _cutToggle.IsChecked == true && _mesh is not null;
        _cutAxis.IsEnabled = _cutSlider.IsEnabled = _cutFlip.IsEnabled = on;
        if (_mesh is not { } mesh || !on)
        {
            _viewport.CutPlane = null;
            _cutLabel.Text = _mesh is null ? string.Empty : "Coupe désactivée. Touche C pour l’activer.";
            _viewport.RequestRender(false);
            return;
        }
        var axis = Math.Max(0, _cutAxis.SelectedIndex) switch { 1 => 0, 2 => 1, _ => 2 };
        var min = axis == 0 ? mesh.Min.X : axis == 1 ? mesh.Min.Y : mesh.Min.Z;
        var max = axis == 0 ? mesh.Max.X : axis == 1 ? mesh.Max.Y : mesh.Max.Z;
        var offset = min + (max - min) * (float)(_cutSlider.Value / 100.0);
        _viewport.CutPlane = new ClipPlane(axis, offset, _cutFlip.IsChecked == true);
        var name = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
        _cutLabel.Text = $"Coupe à {name} = {ModelUnits.Format((offset - min) * MeasureScale)} mm sur {ModelUnits.Format((max - min) * MeasureScale)} mm. La face coupée apparaît en orange.";
        _viewport.RequestRender(false);
    }

    /// <summary>Asks for a second version of the model and shows it over the first, in another colour, with a table of differences.</summary>
    private async Task CompareAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || _mesh is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir l’autre version du modèle",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Modèles 3D (STL, OBJ, 3MF)") { Patterns = ["*.stl", "*.obj", "*.3mf"] }]
        });
        var local = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(local)) return;
        await LoadSecondAsync(local);
    }

    private async Task LoadSecondAsync(string local)
    {
        if (_mesh is null) return;
        var first = _mesh;
        var reference = _original;
        _compareButton.IsEnabled = false;
        _message.Text = "Chargement de l’autre version…";
        try
        {
            var path = ModelFileSelection.Select([local]);
            var (mesh, measures) = await Task.Run(() =>
            {
                var loaded = MeshLoader.Load(path);
                return (loaded, MeshVolume.Compute(loaded));
            });
            if (!ReferenceEquals(first, _mesh) || reference is null) return;
            // the other version goes where the first one was put on the plate, not to a position of its own
            mesh = _orientation.ApplyLike(reference, mesh);
            _second = mesh;
            _secondMeasures = measures;
            _secondName = Path.GetFileName(path);
            // the comparison is drawn by the graphics card only
            if (_renderer.SelectedIndex != 0) _renderer.SelectedIndex = 0;
            _message.Text = $"{mesh.TriangleCount:N0} triangles chargés pour la comparaison.";
            RebuildComparison();
            ShowTab(2);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or FormatException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
        {
            _message.Text = "Impossible d’ouvrir cette version : " + exception.Message;
        }
        finally { _compareButton.IsEnabled = _mesh is not null; }
    }

    private void ClearComparison()
    {
        _second = null;
        _secondMeasures = null;
        _secondName = null;
        _compareDetails.IsVisible = false;
        ApplyComparison();
    }

    private static Control LegendDot(Color color) => new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Builds the legend and the table of differences for the loaded pair.</summary>
    private void RebuildComparison()
    {
        _compareDetails.Children.Clear();
        _compareTable.Children.Clear();
        if (_mesh is not { } first || _second is not { } second || _measures is not { } firstMeasures || _secondMeasures is not { } secondMeasures)
        {
            _compareDetails.IsVisible = false;
            ApplyComparison();
            return;
        }
        var other = Color.FromRgb(0xFF, 0x6B, 0x6B);
        if (PreviewColorParser.TryParse(_hex.Text, out var mine) && Math.Abs(mine.R - other.R) + Math.Abs(mine.G - other.G) + Math.Abs(mine.B - other.B) < 90) other = Color.FromRgb(0x58, 0xA6, 0xFF);
        _viewport.SecondColor = other;
        var firstColor = PreviewColorParser.TryParse(_hex.Text, out var chosen) ? chosen : Color.FromRgb(0x58, 0xA6, 0xFF);

        _showFirst.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { LegendDot(firstColor), new TextBlock { Text = "A : " + Path.GetFileName(_modelPath), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 } } };
        _showSecond.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { LegendDot(other), new TextBlock { Text = "B : " + _secondName, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 } } };

        void Cells(string label, string a, string b, string difference, bool header)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.2*,*,*,1.5*"), ColumnSpacing = 6 };
            string[] values = [label, a, b, difference];
            for (var column = 0; column < 4; column++)
            {
                var text = new TextBlock { Text = values[column], FontSize = 12, FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal };
                // the last column holds the longest text (difference and percentage): let it wrap rather than cut it
                if (column == 3) text.TextWrapping = TextWrapping.Wrap; else text.TextTrimming = TextTrimming.CharacterEllipsis;
                if (header || column == 0) text.Classes.Add("muted");
                ToolTip.SetTip(text, values[column]);
                Grid.SetColumn(text, column);
                row.Children.Add(text);
            }
            _compareTable.Children.Add(row);
        }
        Cells(string.Empty, "A", "B", "B par rapport à A", header: true);
        foreach (var line in MeshComparison.Compare(first, second, firstMeasures, secondMeasures, MeasureScale))
            Cells(line.Label, line.First, line.Second, line.Difference, header: false);

        var remove = new Button { Content = "Retirer la comparaison" };
        remove.Click += async (_, _) =>
        {
            if (await ConfirmRemovalWindow.AskAsync(remove, "Comparaison 3D", "La comparaison sera retirée du visualiseur. Les fichiers 3D seront conservés.")) ClearComparison();
        };
        _compareNote.Text = "Zones rouges (B) : B dépasse A. Zones bleues (A) : A dépasse B. Les surfaces identiques restent bleues.";
        _compareDetails.Children.Add(_showFirst);
        _compareDetails.Children.Add(_showSecond);
        _compareDetails.Children.Add(_alignCentres);
        _compareDetails.Children.Add(_compareTable);
        _compareDetails.Children.Add(_compareNote);
        _compareDetails.Children.Add(remove);
        _compareDetails.IsVisible = true;
        ApplyComparison();
    }

    private void ApplyComparison() => ApplyComparison(_viewport);

    private void ApplyComparison(IMeshViewport viewport)
    {
        UpdateToolPanels();
        viewport.SecondMesh = _second;
        viewport.ShowFirst = _showFirst.IsChecked != false;
        viewport.ShowSecond = _showSecond.IsChecked != false;
        viewport.SecondOffset = _alignCentres.IsChecked == true && _mesh is { } first && _second is { } second ? MeshComparison.CentreOffset(first, second) : default;
        viewport.RequestRender(false);
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

    /// <summary>Applies the unit remembered for this file, else millimetres, and says so.</summary>
    private void ApplyRememberedUnit(string path, MeshModel mesh)
    {
        var stored = ModelUnitStore.Default.Get(path);
        _applyingUnit = true;
        try
        {
            if (stored is not null && stored.StartsWith(ModelUnits.CustomKey + ":", StringComparison.Ordinal)
                && double.TryParse(stored[(ModelUnits.CustomKey.Length + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var custom) && custom > 0)
            {
                _units.SelectedIndex = ModelUnits.All.Count;
                _scale.Text = custom.ToString("G9", CultureInfo.InvariantCulture);
            }
            else
            {
                var unit = ModelUnits.ByKey(stored) ?? ModelUnits.Default;
                _units.SelectedIndex = ModelUnits.All.ToList().IndexOf(unit);
                _scale.Text = unit.MmPerUnit.ToString("G9", CultureInfo.InvariantCulture);
            }
            _scale.IsVisible = _units.SelectedIndex == ModelUnits.All.Count;
        }
        finally { _applyingUnit = false; }
        var warning = ModelUnits.Warning(mesh.Size, MeasureScale);
        _unitsNote.Text = (stored is null ? "Unité supposée : mm (le fichier ne l’indique pas), modifiable ici. " : "Unité mémorisée pour ce fichier. ") + warning;
        RefreshDimensions();
    }

    /// <summary>The user picked a unit: apply it, remember it for this file and recompute what depends on it.</summary>
    private void UnitChosen()
    {
        if (_applyingUnit) return;
        var index = _units.SelectedIndex;
        var custom = index == ModelUnits.All.Count;
        _scale.IsVisible = custom;
        if (!custom && index >= 0)
        {
            _applyingUnit = true;
            try { _scale.Text = ModelUnits.All[index].MmPerUnit.ToString("G9", CultureInfo.InvariantCulture); }
            finally { _applyingUnit = false; }
        }
        if (_modelPath is not null) ModelUnitStore.Default.Set(_modelPath, custom ? ModelUnits.CustomKey + ":" + MeasureScale.ToString("G9", CultureInfo.InvariantCulture) : ModelUnits.All[Math.Max(0, index)].Key);
        if (_mesh is { } mesh) _unitsNote.Text = ModelUnits.Warning(mesh.Size, MeasureScale) ?? "Unité mémorisée pour ce fichier.";
        RefreshMeasures();
        RefreshVolume();
        RefreshDimensions();
        ScheduleSlice();
    }

    private void SaveCustomScale()
    {
        if (_units.SelectedIndex == ModelUnits.All.Count && _modelPath is not null && TryNumber(_scale.Text, out var value))
            ModelUnitStore.Default.Set(_modelPath, ModelUnits.CustomKey + ":" + value.ToString("G9", CultureInfo.InvariantCulture));
    }

    private void RefreshDimensions()
    {
        ApplyGrid();
        if (_mesh is not { } mesh) return;
        var index = _units.SelectedIndex;
        var label = index >= 0 && index < ModelUnits.All.Count ? ModelUnits.All[index].Short : "unités";
        _dimensions.Text = "Dimensions : " + ModelUnits.FormatSize(mesh.Size, MeasureScale, label);
    }

    /// <summary>Shows or hides the floor grid and sizes its cells in round millimetres for the displayed model and unit.</summary>
    private void ApplyGrid(IMeshViewport? viewport = null)
    {
        viewport ??= _viewport;
        var supported = viewport is GpuViewportHost;
        _grid.IsEnabled = supported;
        viewport.ShowGrid = supported && _grid.IsChecked == true;
        if (_mesh is { } mesh)
        {
            var scale = MeasureScale > 0 ? MeasureScale : 1;
            var largest = Math.Max(mesh.Size.X, Math.Max(mesh.Size.Y, mesh.Size.Z)) * scale;
            var step = GroundGrid.NiceSpacingMillimetres(largest);
            viewport.GridSpacing = (float)(step / scale);
            _gridNote.Text = supported
                ? "Carreaux de " + step.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + " mm, posés sous le modèle."
                : "Disponible avec le rendu par carte graphique.";
        }
        else _gridNote.Text = supported ? "La grille se pose sous le modèle." : "Disponible avec le rendu par carte graphique.";
        PushPlate(viewport);
        viewport.RequestRender(false);
    }

    private double MeasureScale => TryNumber(_scale.Text, out var value) ? value : 1;

    private void RefreshMeasures()
    {
        UpdateToolPanels();
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
        var cutPlane = _viewport.CutPlane;
        var point = await Task.Run(() =>
        {
            var (origin, direction) = ViewMath.Ray(mesh, camera, bounds.Width, bounds.Height, click.X, click.Y);
            if (!MeshPicker.TryPick(mesh, origin, direction, out var hit, out var triangle, cutPlane)) return (System.Numerics.Vector3?)null;
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
        if (_second is not null) RebuildComparison();
        _viewport.RequestRender(false);
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0;

    private async Task AnalyzeAsync()
    {
        var mesh = _mesh;
        if (mesh is null || _activeAnalysis is not null) return;
        if (!TryNumber(_scale.Text, out var scale) || !TryNumber(_threshold.Text, out var threshold))
        {
            _analysis.Text = "Saisissez un seuil positif et, pour une unité personnalisée, une échelle positive en mm par unité.";
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
            if (version != _loadVersion || cancellation.IsCancellationRequested || !ReferenceEquals(_mesh, mesh)) return;
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
        catch (OperationCanceledException) { if (version == _loadVersion && ReferenceEquals(_mesh, mesh)) _analysis.Text = "Analyse annulée."; }
        catch (ArgumentException exception) { if (version == _loadVersion && ReferenceEquals(_mesh, mesh)) _analysis.Text = "Analyse indisponible : " + exception.Message; }
        finally
        {
            if (ReferenceEquals(_activeAnalysis, cancellation)) _activeAnalysis = null;
            _cancelAnalysis.IsEnabled = false;
            _analyze.IsEnabled = _mesh is not null && _activeLoad is null;
        }
    }
}

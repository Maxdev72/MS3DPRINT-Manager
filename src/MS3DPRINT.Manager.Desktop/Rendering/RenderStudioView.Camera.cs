using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>Caméra tab, like KeyShot: preset views and the cameras saved for this part, lock, format and recentre.</summary>
public sealed partial class RenderStudioView
{
    private static readonly SavedCamera[] Presets =
        [new("Trois-quarts", -0.6, 0.35, 1, 0, 0), new("Face", 0, 0.1, 1, 0, 0), new("Côté", -Math.PI / 2, 0.1, 1, 0, 0), new("Dessus", -0.6, 1.2, 1, 0, 0), new("Arrière", Math.PI, 0.25, 1, 0, 0)];
    private readonly UniformGrid _presetList = new() { Columns = 3 };
    private readonly StackPanel _cameraList = new() { Spacing = 4 };
    private readonly CheckBox _lockCamera = new() { Content = "Verrouiller la caméra" };
    private readonly ComboBox _thirds = Choice(["Aucune", "Tiers (3 × 3)", "Quarts (4 × 4)"]);
    private readonly Slider _orbit = new() { Minimum = -180, Maximum = 180, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider _height = new() { Minimum = 0, Maximum = 85, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider _distance = new() { Minimum = -2, Maximum = 2, TickFrequency = 0.1, IsSnapToTickEnabled = true };
    private readonly ThirdsGrid _thirdsGrid = new() { IsVisible = false };
    private static readonly string[] GridNames = ["Aucune", "Tiers", "Quarts"];
    /// <summary>Above the preview: one click goes through no grid, thirds and quarters (same choice as the Caméra tab).</summary>
    private readonly Button _gridButton = new() { Padding = new Thickness(12, 8), MinHeight = 40 };
    /// <summary>Everything in the tab that moves the camera: locked with the preview while paused.</summary>
    private readonly StackPanel _cameraMoves = new() { Spacing = 12 };
    private bool _syncingCamera;

    private Control BuildCameraTab()
    {
        var add = new Button { Content = Icons.Label("IconPlus", "Ajouter la vue actuelle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += async (_, _) => await AddCameraAsync();
        var recenter = new Button { Content = "Recentrer", HorizontalAlignment = HorizontalAlignment.Stretch };
        recenter.Click += async (_, _) => { _sceneFrame = null; await UseOrientationAsync(); var c = _viewport.Camera; Apply(new SavedCamera("", c.Yaw, c.Pitch, 1, 0, 0)); };
        _lockCamera.IsCheckedChanged += (_, _) => UpdateViewLock();
        ToolTip.SetTip(_lockCamera, "La souris ne déplace plus la caméra : le cadrage trouvé ne bouge plus par erreur.");
        _thirds.SelectionChanged += (_, _) => ShowGrid();
        _gridButton.Click += (_, _) => _thirds.SelectedIndex = (_thirds.SelectedIndex + 1) % GridNames.Length;
        ToolTip.SetTip(_gridButton, "Grille de composition sur l’aperçu : aucune, tiers ou quarts. Elle n’apparaît pas dans l’image rendue.");
        ShowGrid();
        ToolTip.SetTip(_thirds, "Repères de cadrage sur l’aperçu seulement : ils n’apparaissent pas dans l’image rendue.");
        _frame.Children.Add(_thirdsGrid);
        // Precise framing: the sliders follow the mouse, and move the camera when dragged.
        foreach (var slider in new[] { _orbit, _height, _distance })
            slider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_syncingCamera) CameraFromSliders(); };
        RebuildCameras();
        foreach (var control in new Control[]
                 {
                     FormKit.SectionLabel("Vues"), _presetList,
                     FormKit.SectionLabel("Mes caméras"), _cameraList, add,
                     new TextBlock { Text = "Clic droit sur une de vos caméras : mettre à jour, renommer ou retirer.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                     FormKit.SectionLabel("Réglage précis"),
                     FormKit.Field("Orbite (°)", _orbit, hint: "Tourne autour de la pièce."),
                     FormKit.Field("Hauteur (°)", _height, hint: "0 : au ras du sol ; 85 : presque au-dessus."),
                     FormKit.Field("Distance", _distance, hint: "À gauche : plus près ; à droite : plus loin."), recenter
                 })
            _cameraMoves.Children.Add(control);
        return new StackPanel
        {
            Spacing = 12,
            Children = { _cameraMoves, FormKit.SectionLabel("Cadrage"), FormKit.Field("Format de l’image", _format), FormKit.Field("Grille de composition", _thirds), _lockCamera }
        };
    }

    private void ShowGrid()
    {
        var index = Math.Max(0, _thirds.SelectedIndex);
        _thirdsGrid.Divisions = index + 2;
        _thirdsGrid.IsVisible = index > 0;
        _thirdsGrid.InvalidateVisual();
        _gridButton.Content = Icons.Label("IconWireframe", "Grille : " + GridNames[index]);
        if (index > 0) _gridButton.Classes.Add("selected"); else _gridButton.Classes.Remove("selected");
        Avalonia.Automation.AutomationProperties.SetName(_gridButton, "Grille de composition : " + GridNames[index]);
    }

    /// <summary>The mouse frames the preview unless the camera is locked or the preview paused (pause locks the whole view).</summary>
    private void UpdateViewLock()
    {
        _viewport.Visual.IsHitTestVisible = _lockCamera.IsChecked != true && !_paused;
        _cameraMoves.IsEnabled = _format.IsEnabled = !_paused;
    }

    /// <summary>The sliders show the camera of the view (called whenever it moves).</summary>
    private void CameraToSliders()
    {
        var c = _viewport.Camera;
        _syncingCamera = true;
        try
        {
            _orbit.Value = Math.Round(Math.IEEERemainder(c.Yaw * 180 / Math.PI, 360) / 5) * 5;
            _height.Value = Math.Clamp(Math.Round(c.Pitch * 180 / Math.PI / 5) * 5, 0, 85);
            _distance.Value = Math.Clamp(Math.Round(-Math.Log2(c.Zoom) * 10) / 10, -2, 2);
        }
        finally { _syncingCamera = false; }
    }

    private void CameraFromSliders()
    {
        var c = _viewport.Camera;
        (c.Yaw, c.Pitch, c.Zoom) = (_orbit.Value * Math.PI / 180, _height.Value * Math.PI / 180, Math.Pow(2, -_distance.Value));
        _viewport.RequestRender(false);
        CameraMoved();
    }

    private IReadOnlyList<SavedCamera> SavedCameras
        => _modelPath is { } model && _saved.Cameras.TryGetValue(model, out var cameras) ? cameras : [];

    private void RebuildCameras()
    {
        _cameraList.Children.Clear();
        _presetList.Children.Clear();
        foreach (var preset in Presets) _presetList.Children.Add(CameraButton(preset, saved: false));
        foreach (var camera in SavedCameras) _cameraList.Children.Add(CameraButton(camera, saved: true));
        MarkCamera();
        RebuildSeries();
    }

    private Button CameraButton(SavedCamera camera, bool saved)
    {
        var button = new Button
        {
            Tag = camera, Classes = { "filter-chip" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = saved ? Icons.Label("IconCamera", camera.Name) : new TextBlock { Text = camera.Name, TextTrimming = TextTrimming.CharacterEllipsis },
            Margin = saved ? default : new Thickness(0, 0, 4, 4)
        };
        button.Click += (_, _) => Apply(camera);
        if (saved)
        {
            var update = new MenuItem { Header = "Mettre à jour avec la vue actuelle" };
            update.Click += (_, _) => ReplaceCamera(camera, Current(camera.Name));
            var rename = new MenuItem { Header = "Renommer…" };
            rename.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                var dialog = new TextPromptWindow("Renommer la caméra", "Le cadrage ne change pas.", camera.Name);
                if (await dialog.ShowDialog<bool>(owner)) ReplaceCamera(camera, camera with { Name = dialog.Value });
            };
            var remove = new MenuItem { Header = "Retirer…" };
            remove.Click += async (_, _) =>
            {
                if (await ConfirmRemovalWindow.AskAsync(this, camera.Name, "La caméra enregistrée pour cette pièce sera retirée. Les images déjà enregistrées ne changent pas."))
                    ReplaceCamera(camera, null);
            };
            button.ContextMenu = new ContextMenu { Items = { update, rename, remove } };
        }
        return button;
    }

    private SavedCamera Current(string name)
    {
        var c = _viewport.Camera;
        return new SavedCamera(name, c.Yaw, c.Pitch, c.Zoom, c.PanX, c.PanY);
    }

    private void Apply(SavedCamera camera)
    {
        var c = _viewport.Camera;
        (c.Yaw, c.Pitch, c.Zoom, c.PanX, c.PanY) = (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY);
        _viewport.RequestRender(false);
        CameraMoved();
    }

    private async Task AddCameraAsync()
    {
        if (_modelPath is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new TextPromptWindow("Ajouter une caméra", "Le cadrage actuel est enregistré pour cette pièce.", $"Vue {SavedCameras.Count + 1}");
        if (!await dialog.ShowDialog<bool>(owner)) return;
        StoreCameras([.. SavedCameras, Current(dialog.Value)]);
    }

    private void ReplaceCamera(SavedCamera old, SavedCamera? replacement)
    {
        var list = SavedCameras.ToList();
        var index = list.IndexOf(old);
        if (index < 0) return;
        if (replacement is null) list.RemoveAt(index); else list[index] = replacement;
        StoreCameras(list);
    }

    private void StoreCameras(IReadOnlyList<SavedCamera> cameras)
    {
        if (_modelPath is not { } model) return;
        _saved = _saved with { Cameras = new Dictionary<string, IReadOnlyList<SavedCamera>>(_saved.Cameras) { [model] = cameras } };
        SavePreferences();
        RebuildCameras();
    }

    /// <summary>Highlights the camera matching the current view.</summary>
    private void MarkCamera()
    {
        var c = _viewport.Camera;
        CameraToSliders();
        foreach (var button in _cameraList.Children.OfType<Button>().Concat(_presetList.Children.OfType<Button>()))
        {
            var camera = (SavedCamera)button.Tag!;
            var same = Math.Abs(Math.IEEERemainder(camera.Yaw - c.Yaw, Math.Tau)) < 0.01 && Math.Abs(camera.Pitch - c.Pitch) < 0.01 &&
                Math.Abs(camera.Zoom - c.Zoom) < 0.01 && Math.Abs(camera.PanX - c.PanX) < 0.5 && Math.Abs(camera.PanY - c.PanY) < 0.5;
            if (same) button.Classes.Add("selected"); else button.Classes.Remove("selected");
        }
    }
}

/// <summary>
/// Composition lines over the preview (thirds or quarters), to place the part: white with a dark edge so they read on a light
/// as on a dark studio, and a dot where they cross. Never rendered.
/// </summary>
public sealed class ThirdsGrid : Control
{
    public ThirdsGrid() => IsHitTestVisible = false;

    /// <summary>3 for thirds, 4 for quarters.</summary>
    public int Divisions { get; set; } = 3;

    public override void Render(DrawingContext context)
    {
        var (w, h) = (Bounds.Width, Bounds.Height);
        var shade = new Pen(new SolidColorBrush(Colors.Black, 0.35), 3);
        var line = new Pen(new SolidColorBrush(Colors.White, 0.9), 1.5);
        for (var pass = 0; pass < 2; pass++)
            for (var i = 1; i < Divisions; i++)
            {
                var pen = pass == 0 ? shade : line;
                context.DrawLine(pen, new Point(w * i / Divisions, 0), new Point(w * i / Divisions, h));
                context.DrawLine(pen, new Point(0, h * i / Divisions), new Point(w, h * i / Divisions));
            }
        // The crossings are where the eye lands: the strong points to place the part on.
        for (var i = 1; i < Divisions; i++)
            for (var j = 1; j < Divisions; j++)
                context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Colors.Black, 0.45), 1.5), new Point(w * i / Divisions, h * j / Divisions), 4, 4);
    }
}

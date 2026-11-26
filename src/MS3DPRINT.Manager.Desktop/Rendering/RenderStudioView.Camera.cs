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
    private readonly CheckBox _thirds = new() { Content = "Grille de composition (tiers)" };
    private readonly Slider _orbit = new() { Minimum = -180, Maximum = 180, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider _height = new() { Minimum = 0, Maximum = 85, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider _distance = new() { Minimum = -2, Maximum = 2, TickFrequency = 0.1, IsSnapToTickEnabled = true };
    private readonly ThirdsGrid _thirdsGrid = new() { IsVisible = false };
    private bool _syncingCamera;

    private Control BuildCameraTab()
    {
        var add = new Button { Content = Icons.Label("IconPlus", "Ajouter la vue actuelle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += async (_, _) => await AddCameraAsync();
        var recenter = new Button { Content = "Recentrer", HorizontalAlignment = HorizontalAlignment.Stretch };
        recenter.Click += async (_, _) => { _sceneFrame = null; await UseOrientationAsync(); var c = _viewport.Camera; Apply(new SavedCamera("", c.Yaw, c.Pitch, 1, 0, 0)); };
        _lockCamera.IsCheckedChanged += (_, _) => _viewport.Visual.IsHitTestVisible = _lockCamera.IsChecked != true;
        ToolTip.SetTip(_lockCamera, "La souris ne déplace plus la caméra : le cadrage trouvé ne bouge plus par erreur.");
        _thirds.IsCheckedChanged += (_, _) => _thirdsGrid.IsVisible = _thirds.IsChecked == true;
        ToolTip.SetTip(_thirds, "Repères de cadrage sur l’aperçu seulement : ils n’apparaissent pas dans l’image rendue.");
        _frame.Children.Add(_thirdsGrid);
        // Precise framing: the sliders follow the mouse, and move the camera when dragged.
        foreach (var slider in new[] { _orbit, _height, _distance })
            slider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_syncingCamera) CameraFromSliders(); };
        RebuildCameras();
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.SectionLabel("Vues"), _presetList,
                FormKit.SectionLabel("Mes caméras"), _cameraList, add,
                new TextBlock { Text = "Clic droit sur une de vos caméras : mettre à jour, renommer ou retirer.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                FormKit.SectionLabel("Réglage précis"),
                FormKit.Field("Orbite (°)", _orbit, hint: "Tourne autour de la pièce."),
                FormKit.Field("Hauteur (°)", _height, hint: "0 : au ras du sol ; 85 : presque au-dessus."),
                FormKit.Field("Distance", _distance, hint: "À gauche : plus près ; à droite : plus loin."),
                FormKit.SectionLabel("Cadrage"), FormKit.Field("Format de l’image", _format), _thirds, _lockCamera, recenter
            }
        };
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

/// <summary>Rule-of-thirds lines over the preview, to place the part; never rendered.</summary>
public sealed class ThirdsGrid : Control
{
    public ThirdsGrid() => IsHitTestVisible = false;

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Colors.White, 0.55), 1, dashStyle: DashStyle.Dash);
        var (w, h) = (Bounds.Width, Bounds.Height);
        for (var i = 1; i <= 2; i++)
        {
            context.DrawLine(pen, new Point(w * i / 3, 0), new Point(w * i / 3, h));
            context.DrawLine(pen, new Point(0, h * i / 3), new Point(w, h * i / 3));
        }
    }
}

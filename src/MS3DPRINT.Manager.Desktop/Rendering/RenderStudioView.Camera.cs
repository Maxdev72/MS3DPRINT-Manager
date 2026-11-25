using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>Caméra tab, like KeyShot: preset views and the cameras saved for this part, lock, format and recentre.</summary>
public sealed partial class RenderStudioView
{
    private static readonly SavedCamera[] Presets =
        [new("Trois-quarts", -0.6, 0.35, 1, 0, 0), new("Face", 0, 0.1, 1, 0, 0), new("Côté", -Math.PI / 2, 0.1, 1, 0, 0), new("Dessus", -0.6, 1.2, 1, 0, 0), new("Arrière", Math.PI, 0.25, 1, 0, 0)];
    private readonly StackPanel _cameraList = new() { Spacing = 4 };
    private readonly CheckBox _lockCamera = new() { Content = "Verrouiller la caméra" };

    private Control BuildCameraTab()
    {
        var add = new Button { Content = Icons.Label("IconPlus", "Ajouter la vue actuelle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += async (_, _) => await AddCameraAsync();
        var recenter = new Button { Content = "Recentrer", HorizontalAlignment = HorizontalAlignment.Stretch };
        recenter.Click += async (_, _) => { _sceneFrame = null; await UseOrientationAsync(); var c = _viewport.Camera; Apply(new SavedCamera("", c.Yaw, c.Pitch, 1, 0, 0)); };
        _lockCamera.IsCheckedChanged += (_, _) => _viewport.Visual.IsHitTestVisible = _lockCamera.IsChecked != true;
        ToolTip.SetTip(_lockCamera, "La souris ne déplace plus la caméra : le cadrage trouvé ne bouge plus par erreur.");
        RebuildCameras();
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.SectionLabel("Caméras"), _cameraList, add,
                new TextBlock { Text = "Clic droit sur une de vos caméras : mettre à jour, renommer ou retirer.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                _lockCamera, FormKit.Field("Format de l’image", _format), recenter
            }
        };
    }

    private IReadOnlyList<SavedCamera> SavedCameras
        => _modelPath is { } model && _saved.Cameras.TryGetValue(model, out var cameras) ? cameras : [];

    private void RebuildCameras()
    {
        _cameraList.Children.Clear();
        foreach (var preset in Presets) _cameraList.Children.Add(CameraButton(preset, saved: false));
        foreach (var camera in SavedCameras) _cameraList.Children.Add(CameraButton(camera, saved: true));
        MarkCamera();
        RebuildSeries();
    }

    private Button CameraButton(SavedCamera camera, bool saved)
    {
        var button = new Button
        {
            Tag = camera, Classes = { "filter-chip" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Icons.Label(saved ? "IconCamera" : "IconCube", camera.Name)
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
        foreach (var button in _cameraList.Children.OfType<Button>())
        {
            var camera = (SavedCamera)button.Tag!;
            var same = Math.Abs(Math.IEEERemainder(camera.Yaw - c.Yaw, Math.Tau)) < 0.01 && Math.Abs(camera.Pitch - c.Pitch) < 0.01 &&
                Math.Abs(camera.Zoom - c.Zoom) < 0.01 && Math.Abs(camera.PanX - c.PanX) < 0.5 && Math.Abs(camera.PanY - c.PanY) < 0.5;
            if (same) button.Classes.Add("selected"); else button.Classes.Remove("selected");
        }
    }
}

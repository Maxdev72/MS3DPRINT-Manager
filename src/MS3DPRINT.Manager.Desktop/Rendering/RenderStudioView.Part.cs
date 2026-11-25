using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Estimator;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Pièce tab: open a model and stand it — rings to turn it (free or by steps), automatic straightening on its largest flat base,
/// laying a clicked face on the floor, back to the file. Each part's orientation is remembered.
/// </summary>
public sealed partial class RenderStudioView
{
    private static readonly (string Label, double Degrees)[] Steps = [("Libre", 0), ("5°", 5), ("15°", 15), ("45°", 45), ("90°", 90)];
    private readonly ToggleButton _showGizmo = new() { Content = "Anneaux d’orientation", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleButton _faceMode = new() { Content = "Poser sur une face", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _step = Choice(Steps.Select(s => s.Label));
    private readonly TextBlock _orientationNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private MeshModel? _loaded;
    private Quaternion _turn = Quaternion.Identity;

    private Control BuildPartTab()
    {
        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir un modèle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await BrowseAsync();
        var straighten = new Button { Content = "Redresser automatiquement", HorizontalAlignment = HorizontalAlignment.Stretch };
        straighten.Click += async (_, _) => await StraightenAsync();
        var fileOrientation = new Button { Content = "Orientation du fichier", HorizontalAlignment = HorizontalAlignment.Stretch };
        fileOrientation.Click += async (_, _) => { _turn = Quaternion.Identity; _orientationNote.Text = "Pièce telle qu’elle est dans le fichier."; await UseOrientationAsync(); };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                open, _modelName,
                FormKit.SectionLabel("Orientation"), _showGizmo, FormKit.Field("Pas de rotation", _step, hint: "Faites glisser un anneau : rouge X, vert Y, bleu Z."),
                _faceMode, straighten, fileOrientation, _orientationNote
            }
        };
    }

    private void InitializePart()
    {
        _step.SelectedIndex = Math.Max(0, Array.FindIndex(Steps, s => s.Degrees == _saved.RotationStep));
        _gizmo.Step = Steps[_step.SelectedIndex].Degrees;
        _step.SelectionChanged += (_, _) => { _gizmo.Step = Steps[_step.SelectedIndex].Degrees; SavePreferences(); };
        _showGizmo.IsCheckedChanged += (_, _) => _gizmo.IsVisible = _showGizmo.IsChecked == true && _mesh is not null;
        _faceMode.IsCheckedChanged += (_, _) =>
        {
            _viewport.Visual.Cursor = _faceMode.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
            if (_faceMode.IsChecked == true) _orientationNote.Text = "Cliquez la face de la pièce à poser au sol.";
        };
        _gizmo.Turned += async (axis, degrees) =>
        {
            _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(axis, (float)(degrees * Math.PI / 180))));
            _orientationNote.Text = $"Tournée de {degrees:+0.#;-0.#} ° autour de {(axis == Vector3.UnitX ? "X" : axis == Vector3.UnitY ? "Y" : "Z")}.";
            await UseOrientationAsync();
        };
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
        _modelPath = path;
        _modelName.Text = Path.GetFileName(path);
        ToolTip.SetTip(_modelName, path);
        _image.Source = null;
        var camera = _viewport.Camera;
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = (_saved.Yaw, _saved.Pitch, _saved.Zoom, 0, 0);
        RebuildCameras();
        if (_saved.Orientations.TryGetValue(path, out var known) && known.Length == 4)
        {
            _turn = new Quaternion(known[0], known[1], known[2], known[3]);
            _orientationNote.Text = "Orientation retrouvée telle que vous l’aviez laissée.";
            await UseOrientationAsync();
        }
        else await StraightenAsync();
    }

    /// <summary>Lays the part on its largest flat base (the file's own orientation when it already rests on it).</summary>
    private async Task StraightenAsync()
    {
        if (_loaded is not { } loaded) return;
        _turn = await Task.Run(() => RenderOrientation.AutoStraighten(loaded));
        _orientationNote.Text = _turn.IsIdentity
            ? "La pièce repose déjà sur sa plus grande face plane."
            : "Pièce redressée sur sa plus grande face plane. « Orientation du fichier » annule ce redressement.";
        await UseOrientationAsync();
    }

    /// <summary>A click on the part while « Poser sur une face » is on: that face goes down on the floor.</summary>
    private async void FaceClicked(Point point)
    {
        if (_faceMode.IsChecked != true || _mesh is not { } mesh) return;
        var size = _viewport.Visual.Bounds.Size;
        var (origin, direction) = ViewMath.Ray(mesh, _viewport.Camera, size.Width, size.Height, point.X, point.Y);
        if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var triangle)) return;
        _faceMode.IsChecked = false;
        _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, OrientationMath.Between(mesh.Normals[triangle], -Vector3.UnitZ)));
        _orientationNote.Text = "Face posée au sol.";
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
        _gizmo.Mesh = mesh;
        _gizmo.IsVisible = _showGizmo.IsChecked == true;
        _final.IsEnabled = _session is not null;
        _viewport.SetMesh(mesh);
        _viewport.RequestRender(false);
        ShowEmptyState();
        SavePreferences();
        _generation++;
        await RunPreviewsAsync();
    }
}

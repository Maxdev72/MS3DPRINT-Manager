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
    private readonly CheckBox _faceMode = new() { Content = "Poser sur une face (cliquez-la sur l’image)" };
    private readonly UniformGrid _stepChips = new() { Columns = Steps.Length };
    private readonly TextBlock _orientationAngles = new() { Classes = { "caption" } };
    private double _stepDegrees = 15;
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
                open, _modelName, BuildSceneSection(), BuildSceneFileButtons(),
                FormKit.SectionLabel("Orientation de la pièce sélectionnée"),
                new TextBlock { Text = "Faites glisser un anneau sur l’image (rouge X, vert Y, bleu Z), ou tournez d’un pas avec les boutons.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                FormKit.Field("Pas de rotation", _stepChips), AxisButtons(), _orientationAngles,
                _faceMode, straighten, fileOrientation, _orientationNote
            }
        };
    }

    private void InitializePart()
    {
        // Step chosen in one click; the rings and the axis buttons use it.
        foreach (var (label, degrees) in Steps)
        {
            var chip = new Button { Content = label, Tag = degrees, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            chip.Click += (_, _) => { SetStep(degrees); SavePreferences(); };
            _stepChips.Children.Add(chip);
        }
        SetStep(Steps.Any(s => s.Degrees == _saved.RotationStep) ? _saved.RotationStep : 15);
        _faceMode.IsCheckedChanged += (_, _) =>
        {
            _viewport.Visual.Cursor = _faceMode.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
            if (_faceMode.IsChecked == true) _orientationNote.Text = "Cliquez la face de la pièce à poser au sol.";
        };
        _gizmo.Turned += async (axis, degrees) => await TurnAsync(axis, degrees);
    }

    private void SetStep(double degrees)
    {
        _stepDegrees = degrees;
        _gizmo.Step = degrees;
        foreach (var chip in _stepChips.Children.OfType<Button>())
            if ((double)chip.Tag! == degrees) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
    }

    /// <summary>One row per axis, in the colour of its ring: turn back or forward by the step (a quarter turn when free).</summary>
    private Control AxisButtons()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4 };
        (string Name, Vector3 Axis, string Hex)[] axes = [("X", Vector3.UnitX, "#E5484D"), ("Y", Vector3.UnitY, "#46A758"), ("Z", Vector3.UnitZ, "#3E8BFF")];
        for (var row = 0; row < axes.Length; row++)
        {
            var (name, axis, hex) = axes[row];
            var label = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse(hex)), VerticalAlignment = VerticalAlignment.Center, Width = 16 };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            for (var column = 1; column <= 2; column++)
            {
                var sign = column == 1 ? -1 : 1;
                var button = new Button { Content = sign < 0 ? name + " −" : name + " +", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(button, $"Tourner autour de {name} du pas choisi ({(sign < 0 ? "sens inverse" : "sens horaire")})");
                Avalonia.Automation.AutomationProperties.SetName(button, $"Tourner autour de {name}, {(sign < 0 ? "sens inverse" : "sens horaire")}");
                button.Click += async (_, _) => await TurnAsync(axis, sign * (_stepDegrees > 0 ? _stepDegrees : 90));
                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                grid.Children.Add(button);
            }
        }
        return grid;
    }

    private async Task TurnAsync(Vector3 axis, double degrees)
    {
        _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(axis, (float)(degrees * Math.PI / 180))));
        _orientationNote.Text = $"Tournée de {degrees:+0.#;-0.#} ° autour de {(axis == Vector3.UnitX ? "X" : axis == Vector3.UnitY ? "Y" : "Z")}.";
        await UseOrientationAsync();
    }

    /// <summary>The part's orientation from the file, as three angles (X, then Y, then Z).</summary>
    private void ShowAngles()
    {
        var orientation = new ModelOrientation();
        orientation.Compose(_turn);
        var angles = orientation.EulerDegrees;
        _orientationAngles.Text = $"Orientation : X {ModelOrientation.FormatAngle(angles.X)}° · Y {ModelOrientation.FormatAngle(angles.Y)}° · Z {ModelOrientation.FormatAngle(angles.Z)}°";
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
        ResetScene(loaded, path);
        _parts[0].Zones = _saved.Paints.TryGetValue(path, out var paints) ? [.. paints] : [];
        LoadActive();
        _image.Source = null;
        var camera = _viewport.Camera;
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = (_saved.Yaw, _saved.Pitch, _saved.Zoom, 0, 0);
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
        if (_paintMode.IsChecked == true) { await PaintAtAsync(point); return; }
        if (_mesh is not { } mesh) return;
        var size = _viewport.Visual.Bounds.Size;
        var (origin, direction) = ViewMath.Ray(mesh, _viewport.Camera, size.Width, size.Height, point.X, point.Y);
        if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var triangle)) return;
        // A click on another product of the scene selects it.
        var (clicked, _) = PartAt(triangle);
        if (clicked != _active) SelectPart(clicked);
        if (_faceMode.IsChecked != true) return;
        _faceMode.IsChecked = false;
        _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, OrientationMath.Between(mesh.Normals[triangle], -Vector3.UnitZ)));
        _orientationNote.Text = "Face posée au sol.";
        await UseOrientationAsync();
    }

    private int _sceneVersion;

    /// <summary>Rebuilds the scene after any change to a product (orientation, zones, order, spacing) and renders it again.</summary>
    private async Task UseOrientationAsync()
    {
        if (_parts.Count == 0 || _loaded is null) return;
        StoreActive();
        var version = ++_sceneVersion;
        var snapshot = _parts.Select(p => (p.Loaded, p.Turn, p.Zones.ToList(), p.Shift)).ToList();
        var spacing = (float)_spacing.Value;
        var stem = $"scene-{++_counter}";
        (MeshModel Scene, List<(MeshModel Placed, int First, string Body, List<(PaintZone, string)> Files)> Parts) built;
        try { built = await Task.Run(() => BuildScene(snapshot, spacing, _work, stem)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Impossible de préparer la scène : " + exception.Message);
            return;
        }
        // A newer change finished first, or the scene changed meanwhile: these files are already out of date.
        if (version != _sceneVersion || built.Parts.Count != _parts.Count)
        {
            foreach (var (_, _, body, files) in built.Parts) { TryDelete(body); foreach (var (_, file) in files) TryDelete(file); }
            return;
        }
        for (var i = 0; i < _parts.Count; i++)
        {
            DeleteFiles(_parts[i]);
            (_parts[i].Placed, _parts[i].FirstTriangle, _parts[i].Body, _parts[i].ZoneFiles) = built.Parts[i];
        }
        ApplyRenderParts();
        var mesh = built.Scene;
        _mesh = mesh;
        _gizmo.Mesh = Active!.Placed;
        _gizmo.Frame = mesh;
        _gizmo.IsVisible = CurrentTab == 0;
        ShowAngles();
        RebuildPartList();
        _final.IsEnabled = _live is not null;
        // Loading the mesh into the 3D view resets its camera: the framing is kept, a paint stroke or a turn must not move it.
        var camera = _viewport.Camera;
        var kept = (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY);
        _settingMesh = true;
        try { _viewport.SetMesh(mesh); }
        finally { _settingMesh = false; }
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = kept;
        _viewport.RequestRender(false);
        ShowEmptyState();
        SavePreferences();
        _generation++;
        await RunPreviewsAsync();
    }
}

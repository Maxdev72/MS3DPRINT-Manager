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
    private readonly ToggleButton _transformMode = new() { Content = Icons.Label("IconMove", "Déplacer / tourner"), IsEnabled = false, Padding = new Thickness(12, 8), MinHeight = 40 };
    private readonly TextBlock _positionCoordinates = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _transformHint = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly UniformGrid _stepChips = new() { Columns = Steps.Length };
    private readonly TextBlock _orientationAngles = new() { Classes = { "caption" } };
    private double _stepDegrees = 15;
    private readonly TextBlock _orientationNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private MeshModel? _loaded;
    private Quaternion _turn = Quaternion.Identity;
    private readonly StackPanel _partTools = new() { Spacing = 12, IsEnabled = false };

    private Control BuildPartTab()
    {
        var straighten = new Button { Content = "Redresser automatiquement", HorizontalAlignment = HorizontalAlignment.Stretch };
        straighten.Click += async (_, _) => await StraightenAsync();
        var fileOrientation = new Button { Content = "Orientation du fichier", HorizontalAlignment = HorizontalAlignment.Stretch };
        fileOrientation.Click += async (_, _) => { _turn = Quaternion.Identity; _orientationNote.Text = "Pièce telle qu’elle est dans le fichier."; await UseOrientationAsync(); };
        ToolTip.SetTip(_faceMode, "Activez ce mode, puis cliquez la face à poser au sol dans l’aperçu.");
        _faceMode.Content = "Poser sur une face";
        var secondary = new Expander
        {
            Header = "Autres orientations", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "estimate-section" },
            Content = new StackPanel { Spacing = 8, Children = { _faceMode, straighten, fileOrientation } }
        };
        var floor = new Button { Content = Icons.Label("IconFace", "Poser au sol"), HorizontalAlignment = HorizontalAlignment.Stretch };
        floor.Click += async (_, _) => { PlaceSelectedOnFloor(); _orientationNote.Text = "Pièce posée au sol."; await UseOrientationAsync(); };
        _partTools.Children.Add(_transformHint);
        _partTools.Children.Add(floor);
        _partTools.Children.Add(_positionCoordinates);
        _partTools.Children.Add(_orientationAngles);
        _partTools.Children.Add(new Expander
        {
            Header = "Réglages précis", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "estimate-section" },
            Content = new StackPanel
            {
                Spacing = 12, Children =
                {
                    FormKit.Field("Déplacer d’un pas", PositionButtons()),
                    FormKit.Field("Pas de rotation", _stepChips), AxisButtons()
                }
            }
        });
        _partTools.Children.Add(secondary);
        return new StackPanel { Spacing = 16, Children = { BuildSceneSection(), _partTools, _sceneLayout, _orientationNote } };
    }

    private void InitializePart()
    {
        Avalonia.Automation.AutomationProperties.SetName(_transformMode, "Déplacer / tourner la pièce");
        Avalonia.Automation.AutomationProperties.SetName(_gizmo, "Manipulation de la pièce : flèches X Y Z, poignées de plan et anneaux de rotation");
        ToolTip.SetTip(_transformMode, "Flèches : déplacer · Anneaux : tourner · Maj : pas de déplacement · Échap : quitter");
        _transformMode.IsCheckedChanged += (_, _) =>
        {
            if (_transformMode.IsChecked == true)
            {
                _faceMode.IsChecked = false;
                _paintMode.IsChecked = false;
                if (CurrentTab != 0) ShowTab(0);
            }
            UpdateRotationGizmo();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || _transformMode.IsChecked != true) return;
            _gizmo.Cancel();
            _transformMode.IsChecked = false;
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _shiftStep.SelectionChanged += (_, _) => _gizmo.MoveStep = ShiftSteps[Math.Max(0, _shiftStep.SelectedIndex)];
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
            if (_faceMode.IsChecked == true) _transformMode.IsChecked = false;
            _viewport.Visual.Cursor = _faceMode.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
            if (_faceMode.IsChecked == true) _orientationNote.Text = "Cliquez la face de la pièce à poser au sol.";
        };
        _gizmo.Turned += async (axis, degrees) => await TurnAsync(axis, degrees);
        _gizmo.Moved += async move => await MoveSelectedAsync(move);
        _gizmo.TurnCompleted += async () => await UseOrientationAsync();
    }

    private void UpdateRotationGizmo()
    {
        _gizmo.IsVisible = _transformMode.IsChecked == true && CurrentTab == 0 && _mesh is not null;
        _viewHelp.Text = _gizmo.IsVisible
            ? "Flèches et carrés : déplacer · Anneaux : tourner · Échap : quitter"
            : "Glisser : tourner · Clic droit : déplacer · Molette : zoomer";
        _transformHint.Text = _gizmo.IsVisible
            ? "Faites glisser une flèche, un carré ou un anneau dans l’aperçu. Maj applique le pas de déplacement choisi. Échap annule le geste en cours et quitte l’outil."
            : "Sélectionnez une pièce, puis activez « Déplacer / tourner » au-dessus de l’aperçu.";
    }

    private void SetStep(double degrees)
    {
        _stepDegrees = degrees;
        _gizmo.Step = degrees;
        foreach (var chip in _stepChips.Children.OfType<Button>())
            if ((double)chip.Tag! == degrees) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
    }

    /// <summary>One compact group per axis: turn back or forward by the step (a quarter turn when free).</summary>
    private Control AxisButtons()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
        (string Name, Vector3 Axis, string Hex)[] axes = [("X", Vector3.UnitX, "#E5484D"), ("Y", Vector3.UnitY, "#46A758"), ("Z", Vector3.UnitZ, "#3E8BFF")];
        for (var row = 0; row < axes.Length; row++)
        {
            var (name, axis, hex) = axes[row];
            var label = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse(hex)), VerticalAlignment = VerticalAlignment.Center, Width = 16 };
            var group = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), ColumnSpacing = 4 };
            group.Children.Add(label);
            for (var column = 1; column <= 2; column++)
            {
                var sign = column == 1 ? -1 : 1;
                var button = new Button { Content = Icons.Glyph(sign < 0 ? "IconMinus" : "IconPlus", 16), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(button, $"Tourner autour de {name} du pas choisi ({(sign < 0 ? "sens inverse" : "sens horaire")})");
                Avalonia.Automation.AutomationProperties.SetName(button, $"Tourner autour de {name}, {(sign < 0 ? "sens inverse" : "sens horaire")}");
                button.Click += async (_, _) => await TurnAsync(axis, sign * (_stepDegrees > 0 ? _stepDegrees : 90));
                Grid.SetColumn(button, column);
                group.Children.Add(button);
            }
            Grid.SetColumn(group, row);
            grid.Children.Add(group);
        }
        return grid;
    }

    private async Task TurnAsync(Vector3 axis, double degrees)
    {
        _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(axis, (float)(degrees * Math.PI / 180))));
        _orientationNote.Text = $"Tournée de {degrees:+0.#;-0.#} ° autour de {(axis == Vector3.UnitX ? "X" : axis == Vector3.UnitY ? "Y" : "Z")}.";
        if (_gizmo.IsDragging) await PreviewTransformAsync(); else await UseOrientationAsync();
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
        try { loaded = await Task.Run(() => RenderModelKey.Load(path)); }
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
        PlaceSelectedOnFloor();
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
        if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var triangle)) { _transformMode.IsChecked = false; return; }
        // A click on another product of the scene selects it.
        var (clicked, _) = PartAt(triangle);
        if (clicked != _active) SelectPart(clicked);
        if (Active?.Placed is { } placed && _mesh is { } frame) _highlight.Flash(placed, frame, _viewport.Camera);
        if (_faceMode.IsChecked != true) return;
        _faceMode.IsChecked = false;
        _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, OrientationMath.Between(mesh.Normals[triangle], -Vector3.UnitZ)));
        PlaceSelectedOnFloor();
        _orientationNote.Text = "Face posée au sol.";
        await UseOrientationAsync();
    }

    private int _sceneVersion;
    private bool _transformPreviewPending;
    private Task _transformPreviewTask = Task.CompletedTask;

    private Task PreviewTransformAsync()
    {
        _transformPreviewPending = true;
        return _transformPreviewTask.IsCompleted ? _transformPreviewTask = RunTransformPreviewsAsync() : _transformPreviewTask;
    }

    private async Task RunTransformPreviewsAsync()
    {
        while (_transformPreviewPending && _gizmo.IsDragging)
        {
            _transformPreviewPending = false;
            await UpdateOrientationAsync(true);
        }
    }

    private void PlaceSelectedOnFloor()
    {
        if (Active is not { Anchor: { } anchor } part || _loaded is null) return;
        var turned = ProductRenderer.Turned(_loaded, _turn);
        part.Anchor = new Vector3(anchor.X, anchor.Y, Vector3.Transform(_loaded.Center, _turn).Z - turned.Min.Z);
        part.Shift = new Vector3(part.Shift.X, part.Shift.Y, 0);
    }

    /// <summary>Rebuilds the scene after any change to a product (orientation, zones, order, spacing) and renders it again.</summary>
    private Task UseOrientationAsync() => UpdateOrientationAsync(false);

    private async Task UpdateOrientationAsync(bool interactive)
    {
        if (_parts.Count == 0 || _loaded is null) return;
        if (interactive)
        {
            _image.Opacity = 0;
            _refineCancel?.Cancel();
            _generation++;
        }
        StoreActive();
        var version = ++_sceneVersion;
        var snapshot = _parts.Select(p => (p.Loaded, p.Turn, p.Zones.ToList(), p.Shift, p.Anchor)).ToList();
        var spacing = (float)_spacing.Value;
        var stem = $"scene-{++_counter}";
        (MeshModel Scene, List<(MeshModel Placed, int First, string Body, List<(PaintZone, string)> Files)> Parts) built;
        try { built = await Task.Run(() => BuildScene(snapshot, spacing, interactive ? null : _work, stem)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Impossible de préparer la scène : " + exception.Message);
            return;
        }
        // A newer change finished first, or the scene changed meanwhile: these files are already out of date.
        if (version != _sceneVersion || built.Parts.Count != _parts.Count)
        {
            if (!interactive)
                foreach (var (_, _, body, files) in built.Parts) { TryDelete(body); foreach (var (_, file) in files) TryDelete(file); }
            return;
        }
        _sceneFrame ??= built.Scene;
        var framed = new MeshModel(built.Scene, _sceneFrame);
        if (interactive)
        {
            ShowOrientationMesh(framed);
            ShowAngles();
            return;
        }
        for (var i = 0; i < _parts.Count; i++)
        {
            DeleteFiles(_parts[i]);
            (_parts[i].Placed, _parts[i].FirstTriangle, _parts[i].Body, _parts[i].ZoneFiles) = built.Parts[i];
            _parts[i].Anchor ??= built.Parts[i].Placed.Positions[0]
                - Vector3.Transform(_parts[i].Loaded.Positions[0] - _parts[i].Loaded.Center, _parts[i].Turn) - _parts[i].Shift;
        }
        ApplyRenderParts();
        var mesh = framed;
        _mesh = mesh;
        _gizmo.Mesh = Active!.Placed;
        _gizmo.Origin = Active.Anchor is { } origin ? origin + Active.Shift : null;
        _gizmo.Frame = mesh;
        UpdateRotationGizmo();
        ShowAngles();
        ShowPosition();
        RebuildPartList();
        _final.IsEnabled = _live is not null;
        ShowOrientationMesh(mesh);
        ShowEmptyState();
        SavePreferences();
        _generation++;
        await RunPreviewsAsync();
    }

    private void ShowOrientationMesh(MeshModel mesh)
    {
        // Loading the mesh into the 3D view resets its camera: the framing is kept, a paint stroke or a turn must not move it.
        var camera = _viewport.Camera;
        var kept = (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY);
        _settingMesh = true;
        try { _viewport.SetMesh(mesh); }
        finally { _settingMesh = false; }
        (camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY) = kept;
        _viewport.RequestRender(_gizmo.IsDragging);
    }
}

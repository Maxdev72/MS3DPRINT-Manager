using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Several products in one scene, side by side on the floor. Each keeps its file, orientation, colour, finish and painted zones;
/// the active one is the one the tabs edit (a click on a product in the image selects it). For Blender the active product is
/// the main part (series colours apply to it); the others are sent as extra parts with their own colours.
/// </summary>
public sealed partial class RenderStudioView
{
    private sealed class ScenePart
    {
        public required string Path { get; init; }
        public required MeshModel Loaded { get; init; }
        public Quaternion Turn = Quaternion.Identity;
        public List<PaintZone> Zones = [];
        public string Hex = "#DE4343";
        public string Finish = "matte";
        /// <summary>Moved by hand from its place in the row, on the floor (millimetres; X to the right, Y to the back).</summary>
        public Vector3 Shift;
        public Vector3? Anchor;
        public PaintSelector? Selector;
        public MeshModel? Placed;
        public int FirstTriangle;
        public string? Body;
        public List<(PaintZone Zone, string File)> ZoneFiles = [];
    }

    private readonly List<ScenePart> _parts = [];
    private MeshModel? _sceneFrame;
    private bool _openingScene;
    private int _active;
    private readonly Slider _spacing = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = 10 };
    private readonly StackPanel _partList = new() { Spacing = 4 };
    private readonly TextBlock _sceneTitle = new() { Text = "Pièces de la scène", Classes = { "overline" } };
    private readonly Expander _sceneLayout = new() { Header = "Disposition de la scène", IsExpanded = false, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "estimate-section" } };

    private Control BuildSceneSection()
    {
        _sceneLayout.Content = FormKit.Field("Espacement entre les pièces", _spacing, hint: "Les pièces sont rangées côte à côte. Leur menu permet de changer cet ordre.");
        _spacing.PropertyChanged += async (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty || _parts.Count <= 1 || _openingScene) return;
            foreach (var part in _parts) part.Anchor = null;
            _sceneFrame = null;
            await UseOrientationAsync();
        };
        return new StackPanel
        {
            Spacing = 8,
            Children = { _sceneTitle, new ScrollViewer { Content = _partList, MaxHeight = 176, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } }
        };
    }

    private ScenePart? Active => _parts.Count == 0 ? null : _parts[Math.Clamp(_active, 0, _parts.Count - 1)];

    /// <summary>Keeps what the tabs show for the active product in its scene entry.</summary>
    private void StoreActive()
    {
        if (Active is not { } part) return;
        part.Turn = _turn;
        part.Zones = _zones;
        part.Selector = _selector;
        part.Hex = CurrentHex;
        part.Finish = Finishes[Math.Max(0, _finish.SelectedIndex)].Key;
    }

    /// <summary>Shows the active product in the tabs: its orientation, zones, colour and finish.</summary>
    private void LoadActive()
    {
        if (Active is not { } part) return;
        _loaded = part.Loaded;
        _turn = part.Turn;
        _zones = part.Zones;
        _selector = part.Selector;
        _modelPath = part.Path;
        _finish.SelectedIndex = Math.Max(0, Array.FindIndex(Finishes, f => f.Key == part.Finish));
        SetColor(part.Hex);
        RebuildZoneList();
        RebuildCameras();
        RebuildPartList();
        ShowAngles();
        ShowPosition();
    }

    private void SelectPart(int index)
    {
        if (index == _active || index < 0 || index >= _parts.Count) return;
        StoreActive();
        _active = index;
        LoadActive();
        ApplyRenderParts();
        _gizmo.Mesh = Active!.Placed;
        _gizmo.Origin = Active.Anchor is { } origin ? origin + Active.Shift : null;
        _gizmo.InvalidateVisual();
        SettingsChanged();
    }

    /// <summary>The product a triangle of the whole scene belongs to, and its index inside that product.</summary>
    private (int Part, int Triangle) PartAt(int triangle)
    {
        for (var i = _parts.Count - 1; i >= 0; i--)
            if (triangle >= _parts[i].FirstTriangle) return (i, triangle - _parts[i].FirstTriangle);
        return (0, triangle);
    }

    /// <summary>Starts a scene with this product only (opening a model).</summary>
    private void ResetScene(MeshModel loaded, string path)
    {
        _sceneFrame = null;
        foreach (var part in _parts) DeleteFiles(part);
        _parts.Clear();
        _parts.Add(new ScenePart { Path = path, Loaded = loaded, Hex = CurrentHex, Finish = Finishes[Math.Max(0, _finish.SelectedIndex)].Key });
        _active = 0;
    }

    private async Task BrowseAddAsync()
    {
        if (_parts.Count == 0) { await BrowseAsync(); return; }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ajouter une pièce à la scène",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Modèles 3D (STL, OBJ, 3MF)") { Patterns = ["*.stl", "*.obj", "*.3mf"] }]
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { Length: > 0 } local) await AddPartAsync(local);
    }

    /// <summary>Adds a product next to the others: its remembered orientation and zones, else straightened on its base.</summary>
    public async Task AddPartAsync(string path)
    {
        if (_parts.Count == 0) { await LoadModelAsync(path); return; }
        ShowStatus("Ajout de la pièce…", busy: true);
        MeshModel loaded;
        Quaternion turn;
        try
        {
            loaded = await Task.Run(() => MeshLoader.Load(path));
            turn = _saved.Orientations.TryGetValue(path, out var known) && known.Length == 4
                ? new Quaternion(known[0], known[1], known[2], known[3])
                : await Task.Run(() => RenderOrientation.AutoStraighten(loaded));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            ShowStatus("Impossible d’ouvrir ce modèle : " + exception.Message);
            return;
        }
        StoreActive();
        _parts.Add(new ScenePart
        {
            Path = path, Loaded = loaded, Turn = turn, Hex = CurrentHex, Finish = Finishes[Math.Max(0, _finish.SelectedIndex)].Key,
            Zones = _saved.Paints.TryGetValue(path, out var paints) ? [.. paints] : []
        });
        _active = _parts.Count - 1;
        _sceneFrame = null;
        LoadActive();
        _orientationNote.Text = "Pièce ajoutée à droite des autres.";
        await UseOrientationAsync();
    }

    private void RebuildPartList()
    {
        _partList.Children.Clear();
        _sceneLayout.IsVisible = _parts.Count > 1;
        _partTools.IsEnabled = _parts.Count > 0;
        _transformMode.IsEnabled = _parts.Count > 0;
        _sceneTitle.Text = _parts.Count == 0 ? "Aucune pièce dans la scène" : $"Pièces de la scène ({_parts.Count})";
        for (var i = 0; i < _parts.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,40"), ColumnSpacing = 4 };
            var name = new Button
            {
                Content = new TextBlock { Text = $"{index + 1}. {System.IO.Path.GetFileNameWithoutExtension(_parts[index].Path)}", TextTrimming = TextTrimming.CharacterEllipsis }, Classes = { "filter-chip" },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left
            };
            if (index == _active) name.Classes.Add("selected");
            ToolTip.SetTip(name, _parts[index].Path);
            AutomationProperties.SetName(name, $"Sélectionner la pièce {index + 1} : {System.IO.Path.GetFileNameWithoutExtension(_parts[index].Path)}");
            name.Click += (_, _) => SelectPart(index);
            row.Children.Add(name);
            MenuItem Action(string text, string icon, Func<Task> act, bool enabled)
            {
                var item = new MenuItem { Header = text, Icon = Icons.Glyph(icon, 16), IsEnabled = enabled };
                item.Click += async (_, _) => await act();
                return item;
            }
            var actions = new Button
            {
                Content = Icons.Glyph("IconMore", 16), Padding = new Thickness(8),
                Flyout = new MenuFlyout
                {
                    Items =
                    {
                        Action("Placer avant", "IconArrowLeft", () => MovePartAsync(index, -1), index > 0),
                        Action("Placer après", "IconArrowRight", () => MovePartAsync(index, 1), index < _parts.Count - 1),
                        new Separator(),
                        Action("Retirer de la scène…", "IconTrash", () => RemovePartAsync(index), _parts.Count > 1)
                    }
                }
            };
            AutomationProperties.SetName(actions, $"Actions pour la pièce {index + 1}");
            ToolTip.SetTip(actions, "Changer l’ordre ou retirer cette pièce");
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            _partList.Children.Add(row);
        }
    }

    private async Task MovePartAsync(int index, int step)
    {
        StoreActive();
        var target = index + step;
        (_parts[index], _parts[target]) = (_parts[target], _parts[index]);
        foreach (var part in _parts) part.Anchor = null;
        _sceneFrame = null;
        if (_active == index) _active = target; else if (_active == target) _active = index;
        LoadActive();
        await UseOrientationAsync();
    }

    private async Task RemovePartAsync(int index)
    {
        var name = System.IO.Path.GetFileName(_parts[index].Path);
        if (!await ConfirmRemovalWindow.AskAsync(this, name, "La pièce est retirée de la scène ; son fichier, son orientation et ses zones peintes sont conservés.")) return;
        StoreActive();
        DeleteFiles(_parts[index]);
        _parts.RemoveAt(index);
        _sceneFrame = null;
        _active = Math.Clamp(_active > index ? _active - 1 : _active, 0, _parts.Count - 1);
        LoadActive();
        await UseOrientationAsync();
    }

    private static void DeleteFiles(ScenePart part)
    {
        if (part.Body is { } body) TryDelete(body);
        foreach (var (_, file) in part.ZoneFiles) TryDelete(file);
    }

    /// <summary>
    /// Stands every product, lays them side by side along X (each centred on Y, resting on the floor) and writes their files;
    /// returns the whole scene as one mesh for the 3D view, with where each product's triangles start.
    /// </summary>
    private static (MeshModel Scene, List<(MeshModel Placed, int First, string Body, List<(PaintZone, string)> Files)> Parts) BuildScene(
        IReadOnlyList<(MeshModel Loaded, Quaternion Turn, List<PaintZone> Zones, Vector3 Shift, Vector3? Anchor)> parts, float spacing, string? folder, string stem)
    {
        var built = new List<(MeshModel, int, string, List<(PaintZone, string)>)>();
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var cursor = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            var turned = ProductRenderer.Turned(parts[i].Loaded, parts[i].Turn);
            var pivot = Vector3.Transform(parts[i].Loaded.Center, parts[i].Turn);
            var anchor = parts[i].Anchor ?? new Vector3(cursor + turned.Size.X / 2, 0, turned.Size.Z / 2) + pivot - turned.Center;
            var offset = anchor - pivot + parts[i].Shift;
            cursor = Math.Max(cursor, turned.Max.X + anchor.X - pivot.X + spacing);
            var placed = new MeshModel(turned.Positions.Select(p => p + offset).ToArray(), turned.Indices);
            // During a ring drag only the 3D mesh changes; Blender files are written on release.
            var (body, files) = folder is null ? (string.Empty, new List<(PaintZone, string)>()) : PaintParts.Write(placed, parts[i].Zones, folder, $"{stem}-{i}");
            built.Add((placed, indices.Count / 3, body, files));
            var start = positions.Count;
            positions.AddRange(placed.Positions);
            indices.AddRange(placed.Indices.Select(index => index + start));
        }
        return (parts.Count == 1 ? built[0].Item1 : new MeshModel([.. positions], [.. indices]), built);
    }

    private static readonly double[] ShiftSteps = [1, 5, 10, 50];
    private readonly ComboBox _shiftStep = new() { ItemsSource = ShiftSteps.Select(s => s + " mm").ToList(), SelectedIndex = 1, MinWidth = 96 };

    /// <summary>Arrows that move the selected part on the floor by the chosen step, and a way back to its place in the row.</summary>
    private Control PositionButtons()
    {
        var arrows = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,96,40"), ColumnSpacing = 4 };
        void Add(int column, string icon, string tip, Vector2 direction)
        {
            var button = new Button { Content = Icons.Glyph(icon, 16), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);
            button.Click += async (_, _) => await ShiftActiveAsync(direction * (float)ShiftSteps[Math.Max(0, _shiftStep.SelectedIndex)]);
            Grid.SetColumn(button, column);
            arrows.Children.Add(button);
        }
        Add(0, "IconArrowLeft", "Déplacer la pièce à gauche", new Vector2(-1, 0));
        Add(1, "IconArrowRight", "Déplacer la pièce à droite", new Vector2(1, 0));
        Add(2, "IconChevronUp", "Reculer la pièce (vers le fond)", new Vector2(0, 1));
        Add(3, "IconChevronDown", "Avancer la pièce (vers l’avant)", new Vector2(0, -1));
        var reset = new Button { Content = Icons.Glyph("IconRefresh", 16), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch };
        reset.Click += async (_, _) => { if (Active is { } part) { part.Shift = Vector3.Zero; await UseOrientationAsync(); } };
        ToolTip.SetTip(_shiftStep, "Pas de déplacement");
        AutomationProperties.SetName(_shiftStep, "Pas de déplacement");
        AutomationProperties.SetName(reset, "Réinitialiser la position");
        ToolTip.SetTip(reset, "Replacer la pièce dans la rangée");
        Grid.SetColumn(_shiftStep, 4);
        Grid.SetColumn(reset, 5);
        arrows.Children.Add(_shiftStep);
        arrows.Children.Add(reset);
        return arrows;
    }

    private async Task ShiftActiveAsync(Vector2 move)
        => await MoveSelectedAsync(new Vector3(move, 0));

    private async Task MoveSelectedAsync(Vector3 move)
    {
        if (Active is not { } part) return;
        part.Shift += move;
        _orientationNote.Text = string.Empty;
        ShowPosition();
        if (_gizmo.IsDragging) await PreviewTransformAsync(); else await UseOrientationAsync();
    }

    private void ShowPosition()
    {
        if (Active is not { } part) return;
        _positionCoordinates.Text = $"Déplacement : X {part.Shift.X:0.#} · Y {part.Shift.Y:0.#} · Z {part.Shift.Z:0.#} mm";
    }

    /// <summary>What Blender gets: the active product as the main part, the others (and every painted zone) as extra parts.</summary>
    private void ApplyRenderParts()
    {
        if (Active is not { } active || active.Body is null) return;
        _stlPath = active.Body;
        _zoneFiles = [.. active.ZoneFiles];
        foreach (var other in _parts.Where(p => p != active && p.Body is not null))
        {
            _zoneFiles.Add((new PaintZone(System.IO.Path.GetFileNameWithoutExtension(other.Path), other.Hex, other.Finish, []), other.Body!));
            _zoneFiles.AddRange(other.ZoneFiles);
        }
    }
}

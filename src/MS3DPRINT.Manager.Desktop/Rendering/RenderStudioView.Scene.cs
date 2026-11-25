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
        public Vector2 Shift;
        public PaintSelector? Selector;
        public MeshModel? Placed;
        public int FirstTriangle;
        public string? Body;
        public List<(PaintZone Zone, string File)> ZoneFiles = [];
    }

    private readonly List<ScenePart> _parts = [];
    private int _active;
    private readonly Slider _spacing = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = 10 };
    private readonly StackPanel _partList = new() { Spacing = 4 };
    private readonly StackPanel _sceneFields = new() { Spacing = 8 };

    private Control BuildSceneSection()
    {
        var add = new Button { Content = Icons.Label("IconPlus", "Ajouter une pièce à la scène"), HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += async (_, _) => await BrowseAddAsync();
        _sceneFields.Children.Add(_partList);
        _sceneFields.Children.Add(FormKit.Field("Espacement entre les pièces", _spacing, hint: "Les pièces sont posées côte à côte, de gauche à droite."));
        _sceneFields.Children.Add(FormKit.Field("Position de la pièce sélectionnée", PositionButtons(), hint: "Décale la pièce sur le sol ; « Remettre en place » la repose dans la rangée."));
        _spacing.PropertyChanged += async (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && _parts.Count > 1) await UseOrientationAsync(); };
        return new StackPanel { Spacing = 8, Children = { FormKit.SectionLabel("Scène"), _sceneFields, add } };
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
        _modelName.Text = _parts.Count > 1 ? $"{System.IO.Path.GetFileName(part.Path)} (pièce {_active + 1} sur {_parts.Count})" : System.IO.Path.GetFileName(part.Path);
        ToolTip.SetTip(_modelName, part.Path);
        _finish.SelectedIndex = Math.Max(0, Array.FindIndex(Finishes, f => f.Key == part.Finish));
        SetColor(part.Hex);
        RebuildZoneList();
        RebuildCameras();
        RebuildPartList();
        ShowAngles();
    }

    private void SelectPart(int index)
    {
        if (index == _active || index < 0 || index >= _parts.Count) return;
        StoreActive();
        _active = index;
        LoadActive();
        ApplyRenderParts();
        _gizmo.Mesh = Active!.Placed;
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
        LoadActive();
        _orientationNote.Text = "Pièce ajoutée à droite des autres.";
        await UseOrientationAsync();
    }

    private void RebuildPartList()
    {
        _partList.Children.Clear();
        _sceneFields.IsVisible = _parts.Count > 1;
        for (var i = 0; i < _parts.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 4 };
            var name = new Button
            {
                Content = $"{index + 1}. {System.IO.Path.GetFileNameWithoutExtension(_parts[index].Path)}", Classes = { "filter-chip" },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left
            };
            if (index == _active) name.Classes.Add("selected");
            ToolTip.SetTip(name, "Régler cette pièce (vous pouvez aussi cliquer dessus dans l’image)");
            name.Click += (_, _) => SelectPart(index);
            row.Children.Add(name);
            Control Tool(int column, string text, string tip, Func<Task> act, bool enabled)
            {
                var button = new Button { Content = text, IsEnabled = enabled, MinWidth = 36 };
                ToolTip.SetTip(button, tip);
                AutomationProperties.SetName(button, tip);
                button.Click += async (_, _) => await act();
                Grid.SetColumn(button, column);
                return button;
            }
            row.Children.Add(Tool(1, "←", "Placer plus à gauche", () => MovePartAsync(index, -1), index > 0));
            row.Children.Add(Tool(2, "→", "Placer plus à droite", () => MovePartAsync(index, 1), index < _parts.Count - 1));
            row.Children.Add(Tool(3, "×", "Retirer cette pièce de la scène", () => RemovePartAsync(index), true));
            _partList.Children.Add(row);
        }
    }

    private async Task MovePartAsync(int index, int step)
    {
        StoreActive();
        var target = index + step;
        (_parts[index], _parts[target]) = (_parts[target], _parts[index]);
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
        IReadOnlyList<(MeshModel Loaded, Quaternion Turn, List<PaintZone> Zones, Vector2 Shift)> parts, float spacing, string folder, string stem)
    {
        var built = new List<(MeshModel, int, string, List<(PaintZone, string)>)>();
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var cursor = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            var turned = ProductRenderer.Turned(parts[i].Loaded, parts[i].Turn);
            var offset = new Vector3(cursor - turned.Min.X + parts[i].Shift.X, -(turned.Min.Y + turned.Max.Y) / 2 + parts[i].Shift.Y, -turned.Min.Z);
            cursor += turned.Size.X + spacing;
            var placed = new MeshModel(turned.Positions.Select(p => p + offset).ToArray(), turned.Indices);
            var (body, files) = PaintParts.Write(placed, parts[i].Zones, folder, $"{stem}-{i}");
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
        var arrows = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 4, RowSpacing = 4 };
        void Add(int row, int column, string text, string tip, Vector2 direction)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);
            button.Click += async (_, _) => await ShiftActiveAsync(direction * (float)ShiftSteps[Math.Max(0, _shiftStep.SelectedIndex)]);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            arrows.Children.Add(button);
        }
        Add(0, 1, "↑", "Reculer la pièce (vers le fond)", new Vector2(0, 1));
        Add(1, 0, "←", "Déplacer la pièce à gauche", new Vector2(-1, 0));
        Add(1, 1, "↓", "Avancer la pièce (vers l’avant)", new Vector2(0, -1));
        Add(1, 2, "→", "Déplacer la pièce à droite", new Vector2(1, 0));
        var reset = new Button { Content = "Remettre en place", HorizontalAlignment = HorizontalAlignment.Stretch };
        reset.Click += async (_, _) => { if (Active is { } part) { part.Shift = Vector2.Zero; await UseOrientationAsync(); } };
        ToolTip.SetTip(_shiftStep, "Pas de déplacement");
        var below = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        below.Children.Add(_shiftStep);
        Grid.SetColumn(reset, 1);
        below.Children.Add(reset);
        return new StackPanel { Spacing = 8, Children = { arrows, below } };
    }

    private async Task ShiftActiveAsync(Vector2 move)
    {
        if (Active is not { } part) return;
        part.Shift += move;
        await UseOrientationAsync();
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

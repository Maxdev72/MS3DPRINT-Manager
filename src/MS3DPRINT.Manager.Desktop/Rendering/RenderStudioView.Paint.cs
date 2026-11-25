using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Painting zones on the part (Matière tab): a click paints the whole element (a foot, a button), Alt + click only the face,
/// Ctrl + click erases; each colour keeps its finish. Zones are remembered for each part and rendered as parts of their own.
/// </summary>
public sealed partial class RenderStudioView
{
    private readonly CheckBox _paintMode = new() { Content = "Peindre sur la pièce" };
    private readonly ColorPicker _brushPicker = new() { IsAlphaEnabled = false, IsAlphaVisible = false, Width = 64, MinHeight = 40, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBox _brushHex = new() { Text = "#000000" };
    private readonly Slider _paintAngle = new() { Minimum = 10, Maximum = 60, Value = 30, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly StackPanel _paintFields = new() { Spacing = 8 };
    private readonly StackPanel _zoneList = new() { Spacing = 4 };
    private readonly Button _clearZones = new() { Content = "Tout effacer", HorizontalAlignment = HorizontalAlignment.Stretch };
    private List<PaintZone> _zones = [];
    private List<(PaintZone Zone, string File)> _zoneFiles = [];
    private PaintSelector? _selector;
    private RenderColor _brush = new("Noir", "#000000", "matte");
    private KeyModifiers _clickModifiers;

    private Control BuildPaintSection()
    {
        var brushRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        brushRow.Children.Add(_brushPicker);
        Grid.SetColumn(_brushHex, 1);
        brushRow.Children.Add(_brushHex);
        _paintFields.Children.Add(new TextBlock
        {
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
            Text = "Clic sur la pièce : tout l’élément (un pied, un bouton). Alt + clic : seulement la face. Ctrl + clic : effacer. Les pastilles choisissent la couleur du pinceau."
        });
        _paintFields.Children.Add(FormKit.Field("Pinceau", brushRow, hint: "Avec la finition choisie ci-dessous."));
        _paintFields.Children.Add(FormKit.Field("Arêtes vives à partir de", _paintAngle, hint: "Plus bas : des zones plus petites."));
        _paintFields.Children.Add(_zoneList);
        _paintFields.Children.Add(_clearZones);
        return new StackPanel { Spacing = 8, Children = { FormKit.SectionLabel("Peinture par zones"), _paintMode, _paintFields } };
    }

    private void InitializePaint()
    {
        _paintFields.IsVisible = false;
        _paintMode.IsCheckedChanged += (_, _) =>
        {
            var on = _paintMode.IsChecked == true;
            _paintFields.IsVisible = on;
            if (on) _faceMode.IsChecked = false;
            _viewport.Visual.Cursor = on ? new Cursor(StandardCursorType.Cross) : null;
        };
        _brushPicker.Color = Colors.Black;
        _brushPicker.ColorChanged += (_, e) =>
        {
            if (_syncingColor) return;
            _brushHex.Text = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            _brush = new RenderColor("Couleur " + _brushHex.Text, _brushHex.Text, Finishes[Math.Max(0, _finish.SelectedIndex)].Key);
        };
        _brushHex.LostFocus += (_, _) =>
        {
            var text = _brushHex.Text?.Trim() ?? string.Empty;
            if (!text.StartsWith('#')) text = "#" + text;
            if (PreviewColorParser.TryParse(text, out var color)) SetBrush(new RenderColor("Couleur " + text.ToUpperInvariant(), text.ToUpperInvariant(), Finishes[Math.Max(0, _finish.SelectedIndex)].Key), color);
        };
        _clearZones.Click += async (_, _) =>
        {
            if (_zones.Count == 0 || !await ConfirmRemovalWindow.AskAsync(this, "toutes les zones peintes", "La pièce reprend sa couleur principale partout. Les images déjà enregistrées ne changent pas.")) return;
            _zones = [];
            await ZonesChangedAsync();
        };
        // The click reaches the 3D view after the button is pressed: keep the modifiers of the press.
        _viewport.Visual.AddHandler(PointerPressedEvent, (_, e) => _clickModifiers = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>A swatch clicked while painting sets the brush (colour and finish) instead of the part's colour.</summary>
    private void SetBrush(RenderColor color, Color? parsed = null)
    {
        _brush = color;
        _brushHex.Text = color.Hex;
        _syncingColor = true;
        _brushPicker.Color = parsed ?? Color.Parse(color.Hex);
        _syncingColor = false;
    }

    private async Task PaintAtAsync(Point point)
    {
        if (_mesh is not { } mesh || _loaded is not { } loaded) return;
        var size = _viewport.Visual.Bounds.Size;
        var (origin, direction) = ViewMath.Ray(mesh, _viewport.Camera, size.Width, size.Height, point.X, point.Y);
        if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var hit)) return;
        // Painting another product of the scene selects it first; the stroke then applies to it.
        var (clicked, triangle) = PartAt(hit);
        if (clicked != _active) SelectPart(clicked);
        if (_loaded is not { } painted) return;
        loaded = painted;
        var modifiers = _clickModifiers;
        _selector ??= await Task.Run(() => new PaintSelector(loaded));
        var whole = !modifiers.HasFlag(KeyModifiers.Alt);
        var angle = _paintAngle.Value;
        var picked = await Task.Run(() => _selector.Select(triangle, whole, angle));
        var zones = _zones.Select(z => z with { Triangles = z.Triangles.Where(t => !picked.Contains(t)).ToArray() }).ToList();
        if (!modifiers.HasFlag(KeyModifiers.Control))
        {
            var index = zones.FindIndex(z => z.Hex.Equals(_brush.Hex, StringComparison.OrdinalIgnoreCase) && z.Finish == _brush.Finish);
            if (index >= 0) zones[index] = zones[index] with { Triangles = [.. zones[index].Triangles.Concat(picked)] };
            else zones.Add(new PaintZone(_brush.Label, _brush.Hex, _brush.Finish, [.. picked]));
        }
        _zones = zones.Where(z => z.Triangles.Length > 0).ToList();
        await ZonesChangedAsync();
    }

    private async Task ZonesChangedAsync()
    {
        if (_modelPath is { } model) _saved = _saved with { Paints = new Dictionary<string, IReadOnlyList<PaintZone>>(_saved.Paints) { [model] = _zones } };
        RebuildZoneList();
        await UseOrientationAsync();
    }

    private void RebuildZoneList()
    {
        _zoneList.Children.Clear();
        foreach (var zone in _zones)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = new SolidColorBrush(Color.Parse(zone.Hex)), VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1), BorderBrush = (IBrush?)Application.Current?.FindResource("ControlBorderBrush")
            });
            var finish = Finishes.FirstOrDefault(f => f.Key == zone.Finish).Label ?? zone.Finish;
            var label = new TextBlock { Text = $"{zone.Label} · {finish.ToLowerInvariant()}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var remove = new Button { Content = Icons.Glyph("IconClose", 14), Classes = { "icon-action" } };
            ToolTip.SetTip(remove, "Retirer cette zone");
            AutomationProperties.SetName(remove, "Retirer la zone " + zone.Label);
            remove.Click += async (_, _) =>
            {
                if (!await ConfirmRemovalWindow.AskAsync(this, zone.Label, "Cette zone reprend la couleur principale de la pièce. Les images déjà enregistrées ne changent pas.")) return;
                _zones.Remove(zone);
                await ZonesChangedAsync();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            _zoneList.Children.Add(row);
        }
        _clearZones.IsVisible = _zones.Count > 0;
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>Matière tab: colour picker, the user's colours, the workshop filaments and the bundled palettes, finish and layer lines.</summary>
public sealed partial class RenderStudioView
{
    private readonly ColorPicker _picker = new() { IsAlphaEnabled = false, IsAlphaVisible = false, Width = 64, MinHeight = 40, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBox _hex = new();
    private readonly Button _keepColor = new() { Content = Icons.Glyph("IconPlus", 16) };
    private readonly StackPanel _colorGroups = new() { Spacing = 8 };
    private readonly Button _restoreColors = new() { HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private IReadOnlyList<RenderColor> _workshopColors = [];
    private List<RenderColor> _customColors = [];
    private HashSet<string> _hiddenColors = [];
    private bool _syncingColor;

    private string CurrentHex => _hex.Text?.Trim().ToUpperInvariant() ?? string.Empty;

    private Control BuildMaterialTab()
    {
        var colorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        colorRow.Children.Add(_picker);
        Grid.SetColumn(_hex, 1);
        colorRow.Children.Add(_hex);
        Grid.SetColumn(_keepColor, 2);
        colorRow.Children.Add(_keepColor);
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.Field("Couleur", colorRow, hint: "Nuancier ou code #RRGGBB ; + l’ajoute à vos couleurs. Survolez une pastille : × la retire."),
                _colorGroups, _restoreColors,
                FormKit.Field("Finition", _finish), _glowDark, FormKit.Field("Lignes de couche", _layer)
            }
        };
    }

    private void InitializeMaterial()
    {
        _customColors = [.. _saved.CustomColors ?? []];
        _hiddenColors = [.. _saved.HiddenColors ?? []];
        _hex.Text = PreviewColorParser.TryParse(_saved.Hex, out var savedColor) ? _saved.Hex : RenderPreferences.Default.Hex;
        _picker.Color = PreviewColorParser.TryParse(_hex.Text, out savedColor) ? savedColor : Colors.Red;
        _hex.LostFocus += (_, _) => HexEdited();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { HexEdited(); e.Handled = true; } };
        _picker.ColorChanged += (_, e) =>
        {
            if (_syncingColor) return;
            _hex.Text = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            ColorChanged();
        };
        ToolTip.SetTip(_picker, "Choisir une couleur personnalisée");
        AutomationProperties.SetName(_picker, "Choisir une couleur personnalisée");
        ToolTip.SetTip(_keepColor, "Ajouter cette couleur à mes couleurs");
        AutomationProperties.SetName(_keepColor, "Ajouter cette couleur à mes couleurs");
        _keepColor.Click += async (_, _) => await KeepColorAsync();
        _restoreColors.Click += (_, _) => { _hiddenColors.Clear(); SavePreferences(); RebuildSwatches(); };
        RebuildSwatches();
    }

    /// <summary>The user's colours, then the workshop filaments, then the palettes (Mat, Brillant, Soie, Phosphorescent); removed ones left out.</summary>
    private void RebuildSwatches()
    {
        _colorGroups.Children.Clear();
        var shown = new HashSet<string>();
        void Group(string title, IEnumerable<RenderColor> colors, bool custom, bool expanded)
        {
            var wrap = new WrapPanel();
            foreach (var color in colors)
                if ((custom || !_hiddenColors.Contains(color.Key)) && shown.Add(color.Key)) wrap.Children.Add(Swatch(color, custom));
            if (wrap.Children.Count > 0) _colorGroups.Children.Add(new Expander { Header = $"{title} ({wrap.Children.Count})", Content = wrap, IsExpanded = expanded, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        Group("Mes couleurs", _customColors, custom: true, expanded: true);
        Group("Filaments de l’atelier", _workshopColors, custom: false, expanded: true);
        foreach (var palette in RenderPalettes.All) Group(palette.Name, palette.Colors, custom: false, expanded: palette.Finish == "matte");
        _restoreColors.Content = $"Rétablir les couleurs retirées ({_hiddenColors.Count})";
        _restoreColors.IsVisible = _hiddenColors.Count > 0;
        MarkSwatch();
    }

    /// <summary>A colour dot; hovering shows a small × that removes it (after confirmation).</summary>
    private Control Swatch(RenderColor color, bool custom)
    {
        var swatch = new Button
        {
            Tag = color, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(4),
            CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = Brushes.Transparent, BorderThickness = new Thickness(2),
            Content = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = new SolidColorBrush(Color.Parse(color.Hex)),
                BorderThickness = new Thickness(1), BorderBrush = (IBrush?)Application.Current?.FindResource("ControlBorderBrush")
            }
        };
        ToolTip.SetTip(swatch, custom ? color.Label + " · ma couleur" : color.Label + " · teinte approchée");
        AutomationProperties.SetName(swatch, "Couleur " + color.Label);
        swatch.Click += (_, _) =>
        {
            _finish.SelectedIndex = Math.Max(0, Array.FindIndex(Finishes, f => f.Key == color.Finish));
            SetColor(color.Hex);
        };
        var remove = new Button
        {
            Content = "×", Width = 16, Height = 16, MinWidth = 16, MinHeight = 16, Padding = new Thickness(0), FontSize = Ui.Text.Micro,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(Ui.Radius.Pill),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsVisible = false
        };
        ToolTip.SetTip(remove, "Retirer cette couleur");
        AutomationProperties.SetName(remove, "Retirer la couleur " + color.Label);
        remove.Click += async (_, _) => await RemoveColorAsync(color, custom);
        var holder = new Panel { Margin = new Thickness(0, 0, 8, 8), Children = { swatch, remove } };
        holder.PointerEntered += (_, _) => remove.IsVisible = true;
        holder.PointerExited += (_, _) => remove.IsVisible = false;
        return holder;
    }

    private async Task KeepColorAsync()
    {
        HexEdited();
        if (!PreviewColorParser.TryParse(CurrentHex, out _) || TopLevel.GetTopLevel(this) is not Window owner) return;
        var finish = Finishes[_finish.SelectedIndex];
        var dialog = new TextPromptWindow("Ajouter à mes couleurs", $"{CurrentHex} · {finish.Label}. Elle restera proposée sur cette page.", string.Empty);
        if (!await dialog.ShowDialog<bool>(owner)) return;
        _customColors.RemoveAll(c => c.Label.Equals(dialog.Value, StringComparison.OrdinalIgnoreCase));
        _customColors.Insert(0, new RenderColor(dialog.Value, CurrentHex, finish.Key));
        SavePreferences();
        RebuildSwatches();
    }

    private async Task RemoveColorAsync(RenderColor color, bool custom)
    {
        var detail = custom
            ? "La couleur ne sera plus proposée sur la page Rendu produit. Les images déjà enregistrées ne changent pas."
            : "La couleur sera masquée sur cette page ; « Rétablir les couleurs retirées » la fait revenir. Les images déjà enregistrées ne changent pas.";
        if (!await ConfirmRemovalWindow.AskAsync(this, color.Label, detail)) return;
        if (custom) _customColors.Remove(color);
        else _hiddenColors.Add(color.Key);
        SavePreferences();
        RebuildSwatches();
    }

    private void HexEdited()
    {
        var text = _hex.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('#')) text = "#" + text;
        if (!PreviewColorParser.TryParse(text, out _)) { SettingsChanged(); return; }
        SetColor(text.ToUpperInvariant());
    }

    private void SetColor(string hex)
    {
        _hex.Text = hex.ToUpperInvariant();
        if (PreviewColorParser.TryParse(hex, out var color))
        {
            _syncingColor = true;
            _picker.Color = color;
            _syncingColor = false;
        }
        ColorChanged();
    }

    private void ColorChanged()
    {
        if (PreviewColorParser.TryParse(CurrentHex, out var color)) _viewport.ModelColor = color;
        MarkSwatch();
        SettingsChanged();
    }

    /// <summary>Rings the dots of the current colour and finish.</summary>
    private void MarkSwatch()
    {
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        var finish = _finish.SelectedIndex >= 0 ? Finishes[_finish.SelectedIndex].Key : string.Empty;
        foreach (var swatch in _colorGroups.GetLogicalDescendantsOfType<Button>().Where(b => b.Tag is RenderColor))
        {
            var color = (RenderColor)swatch.Tag!;
            swatch.BorderBrush = color.Hex.Equals(CurrentHex, StringComparison.OrdinalIgnoreCase) && color.Finish == finish ? accent : Brushes.Transparent;
        }
    }
}

internal static class LogicalTreeHelpers
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}

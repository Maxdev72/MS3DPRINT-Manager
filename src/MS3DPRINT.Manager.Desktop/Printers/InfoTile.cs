using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// Icon and label, value (with an optional muted part such as "/ 247") and a short note under it, always three lines
/// high, on a soft rounded surface: the one tile used for the print facts and the temperatures, so every row lines up.
/// </summary>
public sealed class InfoTile : Border
{
    private readonly TextBlock _label = new() { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Run _main = new(), _suffix = new() { FontWeight = FontWeight.Normal, FontSize = Ui.Text.Caption };
    private readonly TextBlock _value = new() { FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };

    public InfoTile(string label, string icon)
    {
        CornerRadius = new(Ui.Radius.Control);
        Padding = new(12, 8);
        Bind(BackgroundProperty, this.GetResourceObservable("NeutralSoftBrush"));
        _suffix.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("MutedTextBrush"));
        _value.Inlines = new InlineCollection { _main, _suffix };
        Child = new StackPanel
        {
            Spacing = 2,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Glyph(icon, 14), _label } }, _value, _note }
        };
        _label.Text = label;
        Set("—");
    }

    public string Value => _main.Text + _suffix.Text;
    public string Note => _note.Text ?? "";

    public void SetLabel(string label) => _label.Text = label;

    public void Set(string value, string? note = null, string? suffix = null)
    {
        _main.Text = value;
        _suffix.Text = suffix ?? "";
        // A non-breaking space keeps the third line, and so the tile's height, when there is nothing to add.
        _note.Text = string.IsNullOrEmpty(note) ? " " : note;
        AutomationProperties.SetName(this, $"{_label.Text} : {value}{suffix} {note}".Trim());
    }
}

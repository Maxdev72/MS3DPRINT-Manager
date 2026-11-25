using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// Label, value and a short note under it, always three lines high: the one tile used for the print facts, the
/// temperatures and the Surveillance summary, so every row lines up.
/// </summary>
public sealed class InfoTile : Border
{
    private readonly TextBlock _label = new() { Classes = { "overline" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };

    public InfoTile(string label)
    {
        CornerRadius = new(Ui.Radius.Control);
        Padding = new(12, 8);
        Bind(BackgroundProperty, this.GetResourceObservable("NeutralSoftBrush"));
        Child = new StackPanel { Spacing = 2, Children = { _label, _value, _note } };
        _label.Text = label;
        Set("—");
    }

    public string Value => _value.Text ?? "";
    public string Note => _note.Text ?? "";

    public void Set(string label, string value, string? note = null)
    {
        _label.Text = label;
        _value.Text = value;
        // A non-breaking space keeps the third line, and so the tile's height, when there is nothing to add.
        _note.Text = string.IsNullOrEmpty(note) ? " " : note;
        AutomationProperties.SetName(this, $"{label} : {value} {note}".Trim());
        ToolTip.SetTip(this, string.IsNullOrEmpty(note) ? value : value + " · " + note);
    }

    public void Set(string value, string? note = null) => Set(_label.Text ?? "", value, note);
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Label / value pairs of a sheet: muted labels in one aligned column, selectable values beside them.</summary>
public static class KeyValueList
{
    public const double LabelWidth = 120;

    public static Grid Create(IEnumerable<(string Label, string? Value)> pairs)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*"), ColumnSpacing = Ui.Space.S4, RowSpacing = Ui.Space.S2, Classes = { "key-values" } };
        foreach (var (label, value) in pairs.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = new TextBlock { Text = label, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, LineHeight = Ui.Text.LineHeights[1] };
            var text = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap, LineHeight = Ui.Text.LineHeights[1] };
            Avalonia.Automation.AutomationProperties.SetName(text, label + " : " + value);
            Grid.SetRow(key, row); Grid.SetRow(text, row); Grid.SetColumn(text, 1);
            grid.Children.Add(key); grid.Children.Add(text);
        }
        grid.IsVisible = grid.RowDefinitions.Count > 0;
        return grid;
    }
}

using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// The one toolbar of every list page: search and filters on the left, count and the « Nouveau … » button on the right.
/// Both sides wrap on narrow windows and stay on the 4 px grid.
/// </summary>
public static class ListToolbar
{
    public const double SearchWidth = 280;

    public static Grid Create(IEnumerable<Control> left, IEnumerable<Control> right)
    {
        var start = new WrapPanel { ItemSpacing = Ui.Space.S2, LineSpacing = Ui.Space.S2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var control in left) { control.VerticalAlignment = VerticalAlignment.Center; start.Children.Add(control); }
        var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Space.S3, VerticalAlignment = VerticalAlignment.Center };
        foreach (var control in right) { control.VerticalAlignment = VerticalAlignment.Center; end.Children.Add(control); }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Ui.Space.S4, Classes = { "list-toolbar" } };
        Grid.SetColumn(end, 1);
        grid.Children.Add(start); grid.Children.Add(end);
        return grid;
    }

    /// <summary>The search field of a list: magnifier, fixed width, same look on every page.</summary>
    public static Border Search(TextBox input)
    {
        input.Classes.Add("search-input");
        var icon = Icons.Glyph("IconSearch", 16);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Ui.Space.S2 };
        Grid.SetColumn(input, 1);
        grid.Children.Add(icon); grid.Children.Add(input);
        return new Border { Classes = { "search-box" }, Height = 40, Width = SearchWidth, Child = grid };
    }

    /// <summary>Takes a control out of its current panel so it can join a toolbar.</summary>
    public static T Detach<T>(T control) where T : Control
    {
        if (control.Parent is Panel panel) panel.Children.Remove(control);
        return control;
    }
}

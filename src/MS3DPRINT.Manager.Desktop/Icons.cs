using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Drawn icons from the theme (one stroke weight everywhere) instead of text glyphs.</summary>
public static class Icons
{
    /// <summary>
    /// Icon drawn on its 24 x 24 grid and scaled as a whole, so every icon keeps the same optical size
    /// (a stretched path would blow up icons whose drawing does not fill the grid, and flatten a row of dots).
    /// </summary>
    public static Control Glyph(string key, double size = 16, bool dots = false)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Classes = { "icon" }, Width = 24, Height = 24, Stretch = Stretch.None,
            StrokeThickness = dots ? 3 : 2
        };
        if (Application.Current?.TryFindResource(key, out var geometry) == true && geometry is Geometry data) path.Data = data;
        return new Viewbox { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Child = path };
    }

    /// <summary>Icon followed by a label, for buttons and menu entries.</summary>
    public static Control Label(string key, string text, double size = 16)
        => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { Glyph(key, size), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
        };
}

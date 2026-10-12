using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Drawn icons from the theme (one stroke weight everywhere) instead of text glyphs.</summary>
public static class Icons
{
    public static Avalonia.Controls.Shapes.Path Glyph(string key, double size = 16, bool dots = false)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" }, Width = size, Height = size };
        if (dots) icon.StrokeThickness = 3;
        if (Application.Current?.TryFindResource(key, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        return icon;
    }

    /// <summary>Icon followed by a label, for buttons and menu entries.</summary>
    public static Control Label(string key, string text, double size = 16)
        => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { Glyph(key, size), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
        };
}

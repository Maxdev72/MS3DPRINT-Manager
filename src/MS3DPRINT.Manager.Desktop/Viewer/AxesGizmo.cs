using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Small X/Y/Z axis indicator drawn over the GPU view (the CPU view draws its own).</summary>
public sealed class AxesGizmo : Control
{
    private readonly ViewCamera _camera;

    public AxesGizmo(ViewCamera camera)
    {
        _camera = camera;
        Width = 108;
        Height = 108;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var origin = new Point(Bounds.Width - 54, Bounds.Height - 54);
        (string Label, Vector3 Direction, Color Color)[] axes =
        [
            ("X", Vector3.UnitX, Color.FromRgb(0xFF, 0x5C, 0x5C)),
            ("Y", Vector3.UnitY, Color.FromRgb(0x5C, 0xD6, 0x7A)),
            ("Z", Vector3.UnitZ, Color.FromRgb(0x5C, 0x9C, 0xFF))
        ];
        foreach (var (label, direction, color) in axes)
        {
            var (x, y) = SoftwareRenderer.ProjectDirection(_camera, direction);
            var end = new Point(origin.X + x * 32, origin.Y + y * 32);
            context.DrawLine(new Pen(new SolidColorBrush(color), 2), origin, end);
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, new SolidColorBrush(color));
            context.DrawText(text, new Point(end.X + (x >= 0 ? 3 : -11), end.Y - 7));
        }
    }
}

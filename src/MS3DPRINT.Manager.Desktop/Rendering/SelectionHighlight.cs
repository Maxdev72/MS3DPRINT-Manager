using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// A light glow over the part just clicked: its outline (the hull of its projected points) tinted with the accent colour,
/// fading out after a moment, so the user sees which part the tabs now edit.
/// </summary>
public sealed class SelectionHighlight : Control
{
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private Point[] _hull = [];

    public SelectionHighlight()
    {
        IsHitTestVisible = false;
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(450) }];
        _fade.Tick += (_, _) => { _fade.Stop(); Opacity = 0; };
    }

    /// <summary>Shows the outline of <paramref name="part"/> as framed by the view of <paramref name="frame"/>, then fades.</summary>
    public void Flash(MeshModel part, MeshModel frame, ViewCamera camera)
    {
        var points = new List<Point>();
        var stride = Math.Max(1, part.Positions.Length / 4000);
        for (var i = 0; i < part.Positions.Length; i += stride)
            if (ViewMath.TryProject(frame, camera, Bounds.Width, Bounds.Height, part.Positions[i], out var x, out var y)) points.Add(new Point(x, y));
        _hull = Hull(points);
        InvalidateVisual();
        Opacity = 1;
        _fade.Stop();
        _fade.Start();
    }

    public void Hide() { _fade.Stop(); Opacity = 0; }

    public override void Render(DrawingContext context)
    {
        if (_hull.Length < 3) return;
        var accent = (Application.Current?.FindResource("FocusBorderBrush") as ISolidColorBrush)?.Color ?? Color.Parse("#3E8BFF");
        var geometry = new PolylineGeometry(_hull, true);
        context.DrawGeometry(new SolidColorBrush(accent, 0.14), new Pen(new SolidColorBrush(accent, 0.9), 2, lineJoin: PenLineJoin.Round), geometry);
    }

    /// <summary>Convex hull (monotone chain), counter-clockwise.</summary>
    internal static Point[] Hull(List<Point> points)
    {
        if (points.Count < 3) return [.. points];
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        static double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new Point[sorted.Length * 2];
        var k = 0;
        foreach (var p in sorted) { while (k >= 2 && Cross(hull[k - 2], hull[k - 1], p) <= 0) k--; hull[k++] = p; }
        for (int i = sorted.Length - 2, lower = k + 1; i >= 0; i--) { var p = sorted[i]; while (k >= lower && Cross(hull[k - 2], hull[k - 1], p) <= 0) k--; hull[k++] = p; }
        return hull[..(k - 1)];
    }
}

using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// A light glow over the part just clicked, on its exact shape: every triangle filled with the accent colour (all turned the same
/// way, so they merge into one silhouette, holes included) and the contour drawn where the surface turns away from the camera.
/// It fades out after a moment, so the user sees which part the tabs now edit.
/// </summary>
public sealed class SelectionHighlight : Control
{
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private Point[] _triangles = [];
    private Point[] _edges = [];
    private int _request;

    public SelectionHighlight()
    {
        IsHitTestVisible = false;
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(450) }];
        _fade.Tick += (_, _) => { _fade.Stop(); Opacity = 0; };
    }

    /// <summary>Shows the shape of <paramref name="part"/> as framed by the view of <paramref name="frame"/>, then fades.</summary>
    public async void Flash(MeshModel part, MeshModel frame, ViewCamera camera)
    {
        var request = ++_request;
        var view = new ViewCamera { Yaw = camera.Yaw, Pitch = camera.Pitch, Zoom = camera.Zoom, PanX = camera.PanX, PanY = camera.PanY };
        var (width, height) = (Bounds.Width, Bounds.Height);
        var (triangles, edges) = await Task.Run(() => Outline(part, frame, view, width, height));
        if (request != _request) return;
        (_triangles, _edges) = (triangles, edges);
        InvalidateVisual();
        Opacity = 1;
        _fade.Stop();
        _fade.Start();
    }

    public void Hide() { _request++; _fade.Stop(); Opacity = 0; }

    public override void Render(DrawingContext context)
    {
        if (_triangles.Length < 3) return;
        var accent = (Application.Current?.FindResource("FocusBorderBrush") as ISolidColorBrush)?.Color ?? Color.Parse("#3E8BFF");
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.SetFillRule(FillRule.NonZero);
            for (var i = 0; i + 2 < _triangles.Length; i += 3) { g.BeginFigure(_triangles[i], true); g.LineTo(_triangles[i + 1]); g.LineTo(_triangles[i + 2]); g.EndFigure(true); }
        }
        context.DrawGeometry(new SolidColorBrush(accent, 0.16), null, fill);
        var outline = new StreamGeometry();
        using (var g = outline.Open())
            for (var i = 0; i + 1 < _edges.Length; i += 2) { g.BeginFigure(_edges[i], false); g.LineTo(_edges[i + 1]); g.EndFigure(false); }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent, 0.95), 2, lineCap: PenLineCap.Round), outline);
    }

    /// <summary>
    /// The projected triangles (each wound the same way on screen) and the contour edges: those between a triangle facing the
    /// camera and one facing away, or on an open border. Vertices are matched by position, as STL files repeat them.
    /// </summary>
    public static (Point[] Triangles, Point[] Edges) Outline(MeshModel part, MeshModel frame, ViewCamera camera, double width, double height)
    {
        var screen = new Point[part.Positions.Length];
        var visible = new bool[screen.Length];
        for (var i = 0; i < screen.Length; i++)
            if (ViewMath.TryProject(frame, camera, width, height, part.Positions[i], out var x, out var y)) (screen[i], visible[i]) = (new Point(x, y), true);
        var triangles = new List<Point>();
        // Per edge: the side of the first triangle (+1 / -1), then 2 once two triangles of opposite sides meet, 0 when alike.
        var sides = new Dictionary<(Vector3, Vector3), int>();
        var indices = part.Indices;
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            if (!visible[a] || !visible[b] || !visible[c]) continue;
            var area = (screen[b].X - screen[a].X) * (screen[c].Y - screen[a].Y) - (screen[b].Y - screen[a].Y) * (screen[c].X - screen[a].X);
            if (Math.Abs(area) < 1e-9) continue;
            var side = Math.Sign(area);
            triangles.Add(screen[a]);
            if (side > 0) { triangles.Add(screen[b]); triangles.Add(screen[c]); } else { triangles.Add(screen[c]); triangles.Add(screen[b]); }
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = Key(part.Positions[p], part.Positions[q]);
                if (!sides.TryGetValue(key, out var seen)) sides[key] = side;
                else if (seen is 1 or -1) sides[key] = seen == side ? 0 : 2;
            }
        }
        var edges = new List<Point>();
        foreach (var ((p, q), value) in sides)
            if (value != 0 && ViewMath.TryProject(frame, camera, width, height, p, out var px, out var py) && ViewMath.TryProject(frame, camera, width, height, q, out var qx, out var qy))
            { edges.Add(new Point(px, py)); edges.Add(new Point(qx, qy)); }
        return ([.. triangles], [.. edges]);
    }

    private static (Vector3, Vector3) Key(Vector3 p, Vector3 q)
        => p.X < q.X || (p.X == q.X && (p.Y < q.Y || (p.Y == q.Y && p.Z <= q.Z))) ? (p, q) : (q, p);
}

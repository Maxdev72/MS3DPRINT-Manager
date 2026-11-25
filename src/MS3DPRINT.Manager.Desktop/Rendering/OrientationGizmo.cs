using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Three rings around the part (X red, Y green, Z blue) drawn over the 3D view: dragging a ring turns the part about that axis,
/// by the chosen step (0 = free). Only the rings catch the mouse; elsewhere the camera keeps moving as usual.
/// </summary>
public sealed class OrientationGizmo : Control, ICustomHitTest
{
    private const int Segments = 72;
    private static readonly (Vector3 Axis, Color Color)[] Rings =
        [(Vector3.UnitX, Color.Parse("#E5484D")), (Vector3.UnitY, Color.Parse("#46A758")), (Vector3.UnitZ, Color.Parse("#3E8BFF"))];

    private int _hover = -1;
    private int _dragging = -1;
    private double _startAngle;
    private double _degrees;
    private Point _pointer;

    public MeshModel? Mesh { get; set; }
    public ViewCamera? Camera { get; set; }
    /// <summary>Snap in degrees; 0 turns freely.</summary>
    public double Step { get; set; } = 15;

    /// <summary>A ring was turned and released: the axis and the angle in degrees.</summary>
    public event Action<Vector3, double>? Turned;

    public bool HitTest(Point point) => IsVisible && (_dragging >= 0 || RingAt(point) >= 0);

    public override void Render(DrawingContext context)
    {
        if (Mesh is null || Camera is null) return;
        for (var ring = 0; ring < Rings.Length; ring++)
        {
            var points = Project(ring);
            if (points.Count < 2) continue;
            var active = ring == _dragging || (_dragging < 0 && ring == _hover);
            var pen = new Pen(new SolidColorBrush(Rings[ring].Color, active ? 1 : 0.75), active ? 5 : 3);
            for (var i = 0; i < points.Count; i++) context.DrawLine(pen, points[i], points[(i + 1) % points.Count]);
        }
        if (_dragging < 0) return;
        var text = new FormattedText(_degrees.ToString("+0.#;-0.#;0", CultureInfo.GetCultureInfo("fr-FR")) + " °", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, Ui.Text.Section, Brushes.White);
        var box = new Rect(_pointer.X + 16, _pointer.Y - 16, text.Width + 16, text.Height + 8);
        context.FillRectangle(new SolidColorBrush(Colors.Black, 0.7), box, 6);
        context.DrawText(text, new Point(box.X + 8, box.Y + 4));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        if (_dragging >= 0)
        {
            var delta = (Angle(_pointer) - _startAngle) * 180 / Math.PI * Direction(_dragging);
            delta = Math.IEEERemainder(delta, 360);
            _degrees = Step > 0 ? Math.Round(delta / Step) * Step : Math.Round(delta, 1);
        }
        else _hover = RingAt(_pointer);
        Cursor = _hover >= 0 || _dragging >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var ring = RingAt(e.GetPosition(this));
        if (ring < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = ring;
        _startAngle = Angle(e.GetPosition(this));
        _degrees = 0;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        var (axis, degrees) = (Rings[_dragging].Axis, _degrees);
        _dragging = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
        if (Math.Abs(degrees) >= 0.5) Turned?.Invoke(axis, degrees);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    private List<Point> Project(int ring)
    {
        var points = new List<Point>(Segments);
        if (Mesh is not { } mesh || Camera is not { } camera) return points;
        var axis = Rings[ring].Axis;
        var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var v = Vector3.Cross(axis, u);
        var radius = mesh.Radius * 0.8f;
        for (var i = 0; i < Segments; i++)
        {
            var a = i * MathF.Tau / Segments;
            if (ViewMath.TryProject(mesh, camera, Bounds.Width, Bounds.Height, mesh.Center + radius * (MathF.Cos(a) * u + MathF.Sin(a) * v), out var x, out var y))
                points.Add(new Point(x, y));
        }
        return points;
    }

    private int RingAt(Point point)
    {
        var best = -1;
        var bestDistance = 8.0;
        for (var ring = 0; ring < Rings.Length; ring++)
        {
            var points = Project(ring);
            for (var i = 0; i < points.Count; i++)
            {
                var distance = DistanceToSegment(point, points[i], points[(i + 1) % points.Count]);
                if (distance < bestDistance) { bestDistance = distance; best = ring; }
            }
        }
        return best;
    }

    private Point Center()
        => Mesh is { } mesh && Camera is { } camera && ViewMath.TryProject(mesh, camera, Bounds.Width, Bounds.Height, mesh.Center, out var x, out var y) ? new Point(x, y) : Bounds.Center;

    private double Angle(Point point)
    {
        var center = Center();
        return Math.Atan2(point.Y - center.Y, point.X - center.X);
    }

    /// <summary>+1 when turning the part positively about the axis turns its ring clockwise on screen, else -1.</summary>
    private double Direction(int ring)
    {
        if (Mesh is not { } mesh || Camera is not { } camera) return 1;
        var axis = Rings[ring].Axis;
        var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var turned = Vector3.Transform(u, Quaternion.CreateFromAxisAngle(axis, 0.1f));
        var radius = mesh.Radius * 0.8f;
        if (!ViewMath.TryProject(mesh, camera, Bounds.Width, Bounds.Height, mesh.Center + radius * u, out var x0, out var y0) ||
            !ViewMath.TryProject(mesh, camera, Bounds.Width, Bounds.Height, mesh.Center + radius * turned, out var x1, out var y1)) return 1;
        var center = Center();
        var change = Math.IEEERemainder(Math.Atan2(y1 - center.Y, x1 - center.X) - Math.Atan2(y0 - center.Y, x0 - center.X), Math.Tau);
        return change >= 0 ? 1 : -1;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        var (x, y) = (a.X + t * dx - p.X, a.Y + t * dy - p.Y);
        return Math.Sqrt(x * x + y * y);
    }
}

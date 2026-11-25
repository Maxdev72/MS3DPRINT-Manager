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
public sealed partial class OrientationGizmo : Control, ICustomHitTest
{
    private const int Segments = 72;
    private static readonly (Vector3 Axis, Color Color)[] Rings =
        [(Vector3.UnitX, Color.Parse("#E5484D")), (Vector3.UnitY, Color.Parse("#46A758")), (Vector3.UnitZ, Color.Parse("#3E8BFF"))];

    private int _hover = -1;
    private int _dragging = -1;
    private double _startAngle;
    private double _degrees;
    private double _rawDegrees, _lastRingAngle;
    private Vector3 _rotationStart;
    private bool _rotationRay;
    private Point _pointer;
    private IPointer? _capturedPointer;

    /// <summary>The part the rings turn (their centre and size).</summary>
    public MeshModel? Mesh { get; set; }
    /// <summary>What the 3D view frames (the whole scene); the rings are projected with it. Null: the part itself.</summary>
    public MeshModel? Frame { get; set; }
    public Vector3? Origin { get; set; }
    public ViewCamera? Camera { get; set; }
    /// <summary>Snap in degrees; 0 turns freely.</summary>
    public double Step { get; set; } = 15;
    public double MoveStep { get; set; } = 5;

    public bool IsDragging => _dragging >= 0;

    /// <summary>A ring moved: the axis and the additional angle since the previous move.</summary>
    public event Action<Vector3, double>? Turned;
    public event Action<Vector3>? Moved;
    public event Action? TurnCompleted;

    public bool HitTest(Point point) => IsVisible && (_dragging >= 0 || HandleAt(point) >= 0);

    public override void Render(DrawingContext context)
    {
        if (Mesh is null || Camera is null) return;
        for (var ring = 0; ring < Rings.Length; ring++)
        {
            var points = Project(ring);
            if (points.Count < 2) continue;
            var active = ring == _dragging || (_dragging < 0 && ring == _hover);
            var pen = new Pen(new SolidColorBrush(Rings[ring].Color, active ? 1 : 0.75), active ? 5 : 3);
            for (var i = 0; i < points.Count; i++)
            {
                context.DrawLine(new Pen(new SolidColorBrush(Colors.Black, 0.5), active ? 7 : 5), points[i], points[(i + 1) % points.Count]);
                context.DrawLine(pen, points[i], points[(i + 1) % points.Count]);
            }
        }
        DrawTranslation(context);
        if (_dragging < 0) return;
        var value = _dragging < 3 ? _degrees.ToString("+0.#;-0.#;0", CultureInfo.GetCultureInfo("fr-FR")) + " °"
            : $"X {_movement.X:0.#} · Y {_movement.Y:0.#} · Z {_movement.Z:0.#} mm";
        var text = new FormattedText(value, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, Ui.Text.Section, Brushes.White);
        var box = new Rect(_pointer.X + 16, _pointer.Y - 16, text.Width + 16, text.Height + 8);
        context.FillRectangle(new SolidColorBrush(Colors.Black, 0.7), box, 6);
        context.DrawText(text, new Point(box.X + 8, box.Y + 4));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        if (_dragging >= 3) MovePointer(_pointer, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (_dragging >= 0)
        {
            var angle = _rotationRay && TryRingVector(_pointer, out var current)
                ? Math.Atan2(Vector3.Dot(Rings[_dragging].Axis, Vector3.Cross(_rotationStart, current)), Vector3.Dot(_rotationStart, current)) * 180 / Math.PI
                : (Angle(_pointer) - _startAngle) * 180 / Math.PI * Direction(_dragging);
            _rawDegrees += Math.IEEERemainder(angle - _lastRingAngle, 360);
            _lastRingAngle = angle;
            var degrees = Step > 0 ? Math.Round(_rawDegrees / Step) * Step : Math.Round(_rawDegrees, 1);
            var change = degrees - _degrees;
            _degrees = degrees;
            if (Math.Abs(change) > 0.001) Turned?.Invoke(Rings[_dragging].Axis, change);
        }
        else _hover = HandleAt(_pointer);
        Cursor = _hover >= 0 || _dragging >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var handle = HandleAt(point);
        if (handle < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragPivot = Pivot;
        _dragRadius = Radius();
        _movement = Vector3.Zero;
        _dragging = handle;
        _startAngle = Angle(point);
        if (handle >= 3) StartMove(point);
        else _rotationRay = TryRingVector(point, out _rotationStart);
        _rawDegrees = _lastRingAngle = 0;
        _degrees = 0;
        e.Pointer.Capture(this);
        _capturedPointer = e.Pointer;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        _dragging = -1;
        e.Pointer.Capture(null);
        _capturedPointer = null;
        e.Handled = true;
        InvalidateVisual();
        TurnCompleted?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging < 0) return;
        _dragging = -1;
        _capturedPointer = null;
        InvalidateVisual();
        TurnCompleted?.Invoke();
    }

    public void Cancel()
    {
        if (_dragging < 0) return;
        if (_dragging < 3) Turned?.Invoke(Rings[_dragging].Axis, -_degrees);
        else Moved?.Invoke(-_movement);
        _dragging = -1;
        _movement = Vector3.Zero;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        InvalidateVisual();
        TurnCompleted?.Invoke();
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
        var radius = Radius();
        for (var i = 0; i < Segments; i++)
        {
            var a = i * MathF.Tau / Segments;
            if (ViewMath.TryProject(Frame ?? mesh, camera, Bounds.Width, Bounds.Height, Pivot + radius * (MathF.Cos(a) * u + MathF.Sin(a) * v), out var x, out var y))
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
        => ProjectPoint(Pivot);

    private double Angle(Point point)
    {
        var center = Center();
        return Math.Atan2(point.Y - center.Y, point.X - center.X);
    }

    private bool TryRingVector(Point point, out Vector3 vector)
    {
        var axis = Rings[_dragging].Axis;
        var (origin, direction) = ViewMath.Ray(Frame ?? Mesh!, Camera!, Bounds.Width, Bounds.Height, point.X, point.Y);
        var denominator = Vector3.Dot(direction, axis);
        vector = Vector3.Zero;
        if (Math.Abs(denominator) < 1e-5f) return false;
        var distance = Vector3.Dot(_dragPivot - origin, axis) / denominator;
        if (distance <= 0) return false;
        vector = origin + direction * distance - _dragPivot;
        if (vector.LengthSquared() < 1e-8f) return false;
        vector = Vector3.Normalize(vector);
        return true;
    }

    /// <summary>+1 when turning the part positively about the axis turns its ring clockwise on screen, else -1.</summary>
    private double Direction(int ring)
    {
        if (Mesh is not { } mesh || Camera is not { } camera) return 1;
        var axis = Rings[ring].Axis;
        var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var turned = Vector3.Transform(u, Quaternion.CreateFromAxisAngle(axis, 0.1f));
        var radius = Radius();
        if (!ViewMath.TryProject(Frame ?? mesh, camera, Bounds.Width, Bounds.Height, Pivot + radius * u, out var x0, out var y0) ||
            !ViewMath.TryProject(Frame ?? mesh, camera, Bounds.Width, Bounds.Height, Pivot + radius * turned, out var x1, out var y1)) return 1;
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

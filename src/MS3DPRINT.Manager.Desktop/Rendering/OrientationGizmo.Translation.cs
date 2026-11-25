using System.Numerics;
using Avalonia;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

public sealed partial class OrientationGizmo
{
    // Handles: rings 0–2, arrows 3–5, planes XY/YZ/ZX 6–8, centre 9.
    private Vector3 _dragPivot, _movement, _planeNormal, _moveStart;
    private float _dragRadius;
    private Vector3 Pivot => IsDragging ? _dragPivot + _movement : Origin ?? Mesh?.Center ?? Vector3.Zero;
    private static Avalonia.Vector Between(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    private Point ProjectPoint(Vector3 point)
        => Mesh is { } mesh && Camera is { } camera && ViewMath.TryProject(Frame ?? mesh, camera, Bounds.Width, Bounds.Height, point, out var x, out var y)
            ? new Point(x, y) : Bounds.Center;

    private float Radius()
    {
        if (IsDragging) return _dragRadius;
        if (Mesh is not { } mesh) return 1;
        var radius = mesh.Radius * 0.8f;
        var centre = ProjectPoint(Pivot);
        var length = Rings.Max(r => Between(ProjectPoint(Pivot + r.Axis * radius), centre).Length);
        return length > 1 ? radius * (float)(100 / length) : radius;
    }

    private Point[] ProjectAxis(int axis)
    {
        var vector = Rings[axis].Axis * Radius();
        return [ProjectPoint(Pivot + vector * 0.48f), ProjectPoint(Pivot + vector * 1.35f)];
    }

    private Point[] ProjectPlane(int plane)
    {
        var a = Rings[plane].Axis * Radius();
        var b = Rings[(plane + 1) % 3].Axis * Radius();
        return [ProjectPoint(Pivot + a * 0.22f + b * 0.22f), ProjectPoint(Pivot + a * 0.44f + b * 0.22f),
            ProjectPoint(Pivot + a * 0.44f + b * 0.44f), ProjectPoint(Pivot + a * 0.22f + b * 0.44f)];
    }

    private int HandleAt(Point point)
    {
        if (Mesh is null || Camera is null) return -1;
        if (Between(point, Center()).Length < 9) return 9;
        for (var i = 0; i < 3; i++)
            if (Inside(point, ProjectPlane(i))) return i + 6;
        var best = -1;
        var distance = 9d;
        for (var i = 0; i < 3; i++)
        {
            var points = ProjectAxis(i);
            if (Between(points[1], points[0]).Length < 12) continue;
            var current = DistanceToSegment(point, points[0], points[1]);
            if (current < distance) { best = i + 3; distance = current; }
        }
        return best >= 0 ? best : RingAt(point);
    }

    private void StartMove(Point point)
    {
        var ray = ViewMath.Ray(Frame ?? Mesh!, Camera!, Bounds.Width, Bounds.Height, point.X, point.Y);
        if (_dragging < 6)
        {
            var axis = Rings[_dragging - 3].Axis;
            var facing = ViewMath.Ray(Frame ?? Mesh!, Camera!, Bounds.Width, Bounds.Height, Center().X, Center().Y).Direction;
            _planeNormal = facing - axis * Vector3.Dot(facing, axis);
        }
        else _planeNormal = _dragging < 9 ? Rings[(_dragging - 6 + 2) % 3].Axis : ray.Direction;
        _planeNormal = _planeNormal.LengthSquared() > 1e-8f ? Vector3.Normalize(_planeNormal) : ray.Direction;
        _moveStart = PlanePoint(point);
    }

    private Vector3 PlanePoint(Point point)
    {
        var (origin, direction) = ViewMath.Ray(Frame ?? Mesh!, Camera!, Bounds.Width, Bounds.Height, point.X, point.Y);
        var denominator = Vector3.Dot(direction, _planeNormal);
        return Math.Abs(denominator) < 1e-6f ? _dragPivot : origin + direction * (Vector3.Dot(_dragPivot - origin, _planeNormal) / denominator);
    }

    private void MovePointer(Point point, bool snap)
    {
        var movement = PlanePoint(point) - _moveStart;
        if (_dragging < 6)
        {
            var axis = Rings[_dragging - 3].Axis;
            movement = axis * Vector3.Dot(movement, axis);
        }
        if (snap && MoveStep > 0)
        {
            float Round(float value) => (float)(Math.Round(value / MoveStep) * MoveStep);
            movement = new Vector3(Round(movement.X), Round(movement.Y), Round(movement.Z));
        }
        var change = movement - _movement;
        _movement = movement;
        if (change.LengthSquared() > 1e-8f) Moved?.Invoke(change);
    }

    private void DrawTranslation(DrawingContext context)
    {
        for (var i = 0; i < 3; i++)
        {
            var color = Rings[i].Color;
            var active = _dragging == i + 3 || (_dragging < 0 && _hover == i + 3);
            var brush = new SolidColorBrush(color);
            var points = ProjectAxis(i);
            var direction = Between(points[1], points[0]);
            if (direction.Length >= 12)
            {
                var unit = direction / direction.Length;
                var perpendicular = new Avalonia.Vector(-unit.Y, unit.X);
                context.DrawLine(new Pen(brush, active ? 6 : 4), points[0], points[1]);
                context.DrawGeometry(brush, null, Polygon([points[1], points[1] - unit * 14 + perpendicular * 6, points[1] - unit * 14 - perpendicular * 6]));
                var label = new FormattedText(i == 0 ? "X" : i == 1 ? "Y" : "Z", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, Ui.Text.Caption, brush);
                context.DrawText(label, points[1] + unit * 8);
            }
            var planeColor = Rings[(i + 2) % 3].Color;
            var planeActive = _dragging == i + 6 || (_dragging < 0 && _hover == i + 6);
            context.DrawGeometry(new SolidColorBrush(planeColor, planeActive ? 0.55 : 0.2), new Pen(new SolidColorBrush(planeColor), 1.5), Polygon(ProjectPlane(i)));
        }
        context.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), Center(), 5, 5);
    }

    private static StreamGeometry Polygon(Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true);
        foreach (var point in points.Skip(1)) context.LineTo(point);
        context.EndFigure(true);
        return geometry;
    }

    private static bool Inside(Point point, Point[] points)
    {
        var sign = 0;
        double area = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (Math.Abs(cross) < 0.001) continue;
            var current = Math.Sign(cross);
            if (sign != 0 && current != sign) return false;
            sign = current;
        }
        return sign != 0 && Math.Abs(area) > 24;
    }
}

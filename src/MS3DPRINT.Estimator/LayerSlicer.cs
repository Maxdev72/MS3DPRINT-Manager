namespace MS3DPRINT.Estimator;

/// <summary>One closed contour of a section: positive area for an outer boundary, negative for a hole; Turning is the sum of its direction changes in radians (2π for a plain outline, more for a toothed one).</summary>
internal readonly record struct SectionLoop(double Area, double Perimeter, double MinX, double MinY, double MaxX, double MaxY, double Turning = 0)
{
    public bool IsHole => Area < 0;
}

/// <summary>Solid area and total contour length of one horizontal section, holes subtracted from the area and added to the length.</summary>
internal readonly record struct LayerSection(double Area, double Perimeter, SectionLoop[] Loops);

internal static class LayerSlicer
{
    private readonly record struct Point(double X, double Y);

    /// <summary>
    /// Cuts the mesh at the middle of every layer. A triangle crossing the plane gives one segment, directed so the material
    /// lies on its left: the shoelace sum of the segments is the section area, and chaining them end to start gives the loops.
    /// Layers are swept from the bottom, so only the triangles crossing the current plane are looked at.
    /// </summary>
    public static LayerSection[] Slice(PrintMesh mesh, double layerHeight, CancellationToken cancellationToken)
    {
        var minZ = (double)mesh.Min.Z;
        var height = (double)mesh.Max.Z - minZ;
        if (!(height > 0)) return [];
        var count = Math.Max(1, (int)Math.Ceiling(height / layerHeight - 1e-9));
        var originX = ((double)mesh.Min.X + mesh.Max.X) / 2;
        var originY = ((double)mesh.Min.Y + mesh.Max.Y) / 2;
        var positions = mesh.Positions;
        var indices = mesh.Indices;

        var triangleCount = mesh.TriangleCount;
        var zMin = new float[triangleCount];
        var zMax = new float[triangleCount];
        var order = new List<int>(triangleCount);
        for (var t = 0; t < triangleCount; t++)
        {
            var a = positions[indices[t * 3]].Z; var b = positions[indices[t * 3 + 1]].Z; var c = positions[indices[t * 3 + 2]].Z;
            zMin[t] = Math.Min(a, Math.Min(b, c));
            zMax[t] = Math.Max(a, Math.Max(b, c));
            if (zMax[t] > zMin[t]) order.Add(t);
        }
        var sorted = order.ToArray();
        Array.Sort(sorted.Select(t => zMin[t]).ToArray(), sorted);

        var sections = new LayerSection[count];
        var active = new List<int>();
        var next = 0;
        double[] x1 = new double[256], y1 = new double[256], x2 = new double[256], y2 = new double[256];
        var starts = new Dictionary<Point, int>();
        Span<double> px = stackalloc double[2];
        Span<double> py = stackalloc double[2];

        for (var layer = 0; layer < count; layer++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var z = minZ + (layer + 0.5) * layerHeight;
            while (next < sorted.Length && zMin[sorted[next]] <= z) active.Add(sorted[next++]);

            var segments = 0;
            var kept = 0;
            double area = 0, perimeter = 0;
            for (var slot = 0; slot < active.Count; slot++)
            {
                var t = active[slot];
                if (zMax[t] <= z) continue;
                active[kept++] = t;
                var a = positions[indices[t * 3]];
                var b = positions[indices[t * 3 + 1]];
                var c = positions[indices[t * 3 + 2]];
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                var nx = uy * vz - uz * vy;
                var ny = uz * vx - ux * vz;

                var found = 0;
                for (var edge = 0; edge < 3 && found < 2; edge++)
                {
                    var from = edge == 0 ? a : edge == 1 ? b : c;
                    var to = edge == 0 ? b : edge == 1 ? c : a;
                    if ((from.Z > z) == (to.Z > z)) continue;
                    // the lower end first, so the two triangles sharing the edge compute the very same point
                    if (from.Z > to.Z || (from.Z == to.Z && (from.X > to.X || (from.X == to.X && from.Y > to.Y)))) (from, to) = (to, from);
                    var s = (z - from.Z) / ((double)to.Z - from.Z);
                    px[found] = from.X + s * ((double)to.X - from.X) - originX;
                    py[found] = from.Y + s * ((double)to.Y - from.Y) - originY;
                    found++;
                }
                if (found < 2) continue;
                double sx1 = px[0], sy1 = py[0], sx2 = px[1], sy2 = py[1];
                if ((sx2 - sx1) * -ny + (sy2 - sy1) * nx < 0) { (sx1, sx2) = (sx2, sx1); (sy1, sy2) = (sy2, sy1); }
                if (segments == x1.Length)
                {
                    Array.Resize(ref x1, segments * 2); Array.Resize(ref y1, segments * 2);
                    Array.Resize(ref x2, segments * 2); Array.Resize(ref y2, segments * 2);
                }
                x1[segments] = sx1; y1[segments] = sy1; x2[segments] = sx2; y2[segments] = sy2;
                segments++;
                area += 0.5 * (sx1 * sy2 - sx2 * sy1);
                perimeter += Math.Sqrt((sx2 - sx1) * (sx2 - sx1) + (sy2 - sy1) * (sy2 - sy1));
            }
            active.RemoveRange(kept, active.Count - kept);
            sections[layer] = new LayerSection(area, perimeter, Chain(x1, y1, x2, y2, segments, starts, area, perimeter));
        }
        return sections;
    }

    /// <summary>
    /// Joins the segments into closed contours. When a point starts two segments the mesh is not a clean surface there:
    /// the section is then kept as one single contour, which still gives the right area and length.
    /// </summary>
    private static SectionLoop[] Chain(double[] x1, double[] y1, double[] x2, double[] y2, int count, Dictionary<Point, int> starts, double totalArea, double totalPerimeter)
    {
        if (count == 0) return [];
        starts.Clear();
        for (var i = 0; i < count; i++)
            if (!starts.TryAdd(new Point(x1[i], y1[i]), i))
                return [new SectionLoop(totalArea, totalPerimeter, double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity)];

        var visited = new bool[count];
        var loops = new List<SectionLoop>();
        for (var first = 0; first < count; first++)
        {
            if (visited[first]) continue;
            double area = 0, length = 0, minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            var current = first;
            double turning = 0, previousX = 0, previousY = 0;
            var hasPrevious = false;
            while (true)
            {
                visited[current] = true;
                double directionX = x2[current] - x1[current], directionY = y2[current] - y1[current];
                if (directionX * directionX + directionY * directionY > 1e-18)
                {
                    if (hasPrevious) turning += Math.Abs(Math.Atan2(previousX * directionY - previousY * directionX, previousX * directionX + previousY * directionY));
                    previousX = directionX; previousY = directionY; hasPrevious = true;
                }
                area += 0.5 * (x1[current] * y2[current] - x2[current] * y1[current]);
                length += Math.Sqrt((x2[current] - x1[current]) * (x2[current] - x1[current]) + (y2[current] - y1[current]) * (y2[current] - y1[current]));
                minX = Math.Min(minX, x1[current]); maxX = Math.Max(maxX, x1[current]);
                minY = Math.Min(minY, y1[current]); maxY = Math.Max(maxY, y1[current]);
                if (!starts.TryGetValue(new Point(x2[current], y2[current]), out var follow) || visited[follow]) break;
                current = follow;
            }
            loops.Add(new SectionLoop(area, length, minX, minY, maxX, maxY, turning));
        }
        return loops.ToArray();
    }
}

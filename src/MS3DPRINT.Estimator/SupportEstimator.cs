using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>
/// Volume of material the supports hold up. Every face leaning out further than the threshold is sampled; under each sample
/// a ray goes down to the first surface it meets (or the bed), and the column between them is what must be supported.
/// </summary>
internal static class SupportEstimator
{
    private const int GridCells = 96;
    private const int MaxSamplesPerSide = 16;

    private const int MaxPoints = 100_000;

    /// <summary>Volume of the columns to hold up (mm³, before applying the support filling), the horizontal area of the overhangs (mm²) and a sample of points under which support is needed.</summary>
    public readonly record struct Analysis(double ColumnVolume, double OverhangArea, List<Vector3> Points);

    public static double ColumnVolume(PrintMesh mesh, double angleDegrees, double layerHeight, CancellationToken cancellationToken) =>
        Analyse(mesh, angleDegrees, layerHeight, cancellationToken).ColumnVolume;

    public static Analysis Analyse(PrintMesh mesh, double angleDegrees, double layerHeight, CancellationToken cancellationToken)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        var triangleCount = mesh.TriangleCount;
        var threshold = -Math.Sin(angleDegrees * Math.PI / 180);
        var cell = Math.Max(Math.Max((double)mesh.Size.X, mesh.Size.Y) / GridCells, 1e-3);
        var grid = new TriangleGrid(mesh, cell, cancellationToken);

        double volume = 0, area = 0;
        var points = new List<Vector3>();
        for (var t = 0; t < triangleCount; t++)
        {
            if ((t & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = positions[indices[t * 3]];
            var b = positions[indices[t * 3 + 1]];
            var c = positions[indices[t * 3 + 2]];
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            var nx = uy * vz - uz * vy;
            var ny = uz * vx - ux * vz;
            var nz = ux * vy - uy * vx;
            var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (!(length > 0) || nz / length >= threshold) continue;

            // the face is cut into side × side equal pieces, each sampled at its centre
            var projected = Math.Abs(nz) / 2;
            var side = Math.Clamp((int)Math.Ceiling(Math.Sqrt(projected / (cell * cell))), 1, MaxSamplesPerSide);
            var share = projected / (side * side);
            for (var i = 0; i < side; i++)
                for (var j = 0; i + j < side; j++)
                {
                    Add(Column(grid, a, b, c, (i + 1.0 / 3) / side, (j + 1.0 / 3) / side, t, layerHeight), share, ref volume, ref area, points);
                    if (i + j < side - 1) Add(Column(grid, a, b, c, (i + 2.0 / 3) / side, (j + 2.0 / 3) / side, t, layerHeight), share, ref volume, ref area, points);
                }
        }
        return new Analysis(volume, area, points);
    }

    private static void Add((double Height, Vector3 Point) column, double share, ref double volume, ref double area, List<Vector3> points)
    {
        if (column.Height <= 0) return;
        volume += share * column.Height;
        area += share;
        if (points.Count < MaxPoints) points.Add(column.Point);
    }

    /// <summary>Height to hold up under the point of the triangle at barycentric coordinates (u, v), and that point; no height when it sits on the bed.</summary>
    private static (double Height, Vector3 Point) Column(TriangleGrid grid, Vector3 a, Vector3 b, Vector3 c, double u, double v, int triangle, double layerHeight)
    {
        var w = 1 - u - v;
        double x = w * a.X + u * b.X + v * c.X, y = w * a.Y + u * b.Y + v * c.Y, z = w * a.Z + u * b.Z + v * c.Z;
        var height = z - grid.SurfaceBelow(x, y, z, triangle);
        return (height > layerHeight ? height : 0, new Vector3((float)x, (float)y, (float)z));
    }

    /// <summary>Triangles listed per square cell of the ground plane, so a vertical ray only tests the triangles of its own column.</summary>
    private sealed class TriangleGrid
    {
        private readonly PrintMesh _mesh;
        private readonly double _cell;
        private readonly int _columns, _rows;
        private readonly int[] _starts, _items;

        public TriangleGrid(PrintMesh mesh, double cell, CancellationToken cancellationToken)
        {
            _mesh = mesh;
            _cell = cell;
            _columns = (int)Math.Ceiling(mesh.Size.X / cell) + 1;
            _rows = (int)Math.Ceiling(mesh.Size.Y / cell) + 1;
            _starts = new int[_columns * _rows + 1];
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                if ((t & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                var (c0, c1, r0, r1) = Range(t);
                for (var r = r0; r <= r1; r++)
                    for (var c = c0; c <= c1; c++)
                        _starts[r * _columns + c + 1]++;
            }
            for (var i = 0; i < _columns * _rows; i++) _starts[i + 1] += _starts[i];
            _items = new int[_starts[^1]];
            var next = (int[])_starts.Clone();
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                var (c0, c1, r0, r1) = Range(t);
                for (var r = r0; r <= r1; r++)
                    for (var c = c0; c <= c1; c++)
                        _items[next[r * _columns + c]++] = t;
            }
        }

        private (int C0, int C1, int R0, int R1) Range(int triangle)
        {
            var a = _mesh.Positions[_mesh.Indices[triangle * 3]];
            var b = _mesh.Positions[_mesh.Indices[triangle * 3 + 1]];
            var c = _mesh.Positions[_mesh.Indices[triangle * 3 + 2]];
            var minX = Math.Min(a.X, Math.Min(b.X, c.X)) - _mesh.Min.X;
            var maxX = Math.Max(a.X, Math.Max(b.X, c.X)) - _mesh.Min.X;
            var minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)) - _mesh.Min.Y;
            var maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - _mesh.Min.Y;
            return (Math.Clamp((int)(minX / _cell), 0, _columns - 1), Math.Clamp((int)(maxX / _cell), 0, _columns - 1),
                    Math.Clamp((int)(minY / _cell), 0, _rows - 1), Math.Clamp((int)(maxY / _cell), 0, _rows - 1));
        }

        /// <summary>Height of the first surface under the point, or the bed when nothing is there.</summary>
        public double SurfaceBelow(double x, double y, double z, int self)
        {
            var column = Math.Clamp((int)((x - _mesh.Min.X) / _cell), 0, _columns - 1);
            var row = Math.Clamp((int)((y - _mesh.Min.Y) / _cell), 0, _rows - 1);
            double best = _mesh.Min.Z;
            var positions = _mesh.Positions;
            var indices = _mesh.Indices;
            for (var k = _starts[row * _columns + column]; k < _starts[row * _columns + column + 1]; k++)
            {
                var t = _items[k];
                if (t == self) continue;
                var a = positions[indices[t * 3]];
                var b = positions[indices[t * 3 + 1]];
                var c = positions[indices[t * 3 + 2]];
                double det = (double)(b.Y - c.Y) * (a.X - c.X) + (double)(c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(det) < 1e-12) continue;
                var l1 = ((double)(b.Y - c.Y) * (x - c.X) + (double)(c.X - b.X) * (y - c.Y)) / det;
                var l2 = ((double)(c.Y - a.Y) * (x - c.X) + (double)(a.X - c.X) * (y - c.Y)) / det;
                var l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                var surface = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                if (surface < z - 1e-3 && surface > best) best = surface;
            }
            return best;
        }
    }
}

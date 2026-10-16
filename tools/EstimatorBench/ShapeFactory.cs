using System.Buffers.Binary;
using System.Numerics;

namespace EstimatorBench;

/// <summary>Reference shapes with a known geometry, written as binary STL so Bambu Studio and the estimator read the same file.</summary>
internal static class ShapeFactory
{
    public static IReadOnlyList<(string Name, Func<(List<Vector3> Positions, List<int> Indices)> Build)> All() =>
    [
        ("cube-20", () => Box(0, 0, 0, 20, 20, 20)),
        ("cube-30", () => Box(0, 0, 0, 30, 30, 30)),
        ("plaque-80x40x3", () => Box(0, 0, 0, 80, 40, 3)),
        ("cylindre-r10-h30", () => Revolve([(0, 0), (10, 0), (10, 30), (0, 30)])),
        ("sphere-r15", () => Revolve(Arc(0, 15, 15, -90, 90, 24))),
        ("tube-r15-r12-h40", () => Revolve([(12, 0), (15, 0), (15, 40), (12, 40)])),
        ("tore-R20-r5", () => Revolve(Arc(20, 5, 5, 0, 360, 24, closed: true))),
        ("boite-creuse-30", HollowBox),
        ("cone-inverse-45", () => Revolve([(0, 0), (40, 30), (0, 30)])),
        ("champignon", () => Revolve([(0, 0), (4, 0), (4, 20), (25, 20), (25, 24), (0, 24)]))
    ];

    public static void WriteStl(string path, List<Vector3> positions, List<int> indices)
    {
        var bytes = new byte[84 + 50 * (indices.Count / 3)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)(indices.Count / 3));
        for (var t = 0; t < indices.Count / 3; t++)
        {
            var offset = 84 + t * 50 + 12;
            for (var corner = 0; corner < 3; corner++)
            {
                var point = positions[indices[t * 3 + corner]];
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + corner * 12), point.X);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + corner * 12 + 4), point.Y);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + corner * 12 + 8), point.Z);
            }
        }
        File.WriteAllBytes(path, bytes);
    }

    private static (List<Vector3>, List<int>) Box(float x0, float y0, float z0, float x1, float y1, float z1, List<Vector3>? positions = null, List<int>? indices = null, bool inside = false)
    {
        positions ??= [];
        indices ??= [];
        var o = positions.Count;
        positions.AddRange([new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)]);
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        for (var i = 0; i < faces.Length; i += 3)
        {
            indices.Add(faces[i] + o);
            indices.Add(faces[inside ? i + 2 : i + 1] + o);
            indices.Add(faces[inside ? i + 1 : i + 2] + o);
        }
        return (positions, indices);
    }

    private static (List<Vector3>, List<int>) HollowBox()
    {
        var (positions, indices) = Box(0, 0, 0, 30, 30, 30);
        return Box(2, 2, 2, 28, 28, 28, positions, indices, inside: true);
    }

    /// <summary>Points (radius, height) of a circle arc, lifted so the lowest point sits on the bed.</summary>
    private static List<(double R, double Z)> Arc(double centreR, double centreZ, double radius, double from, double to, int steps, bool closed = false)
    {
        var points = new List<(double, double)>();
        var count = closed ? steps : steps + 1;
        for (var i = 0; i < count; i++)
        {
            var angle = (from + (to - from) * i / steps) * Math.PI / 180;
            var r = centreR + radius * Math.Cos(angle);
            points.Add((r < 1e-6 ? 0 : r, centreZ + radius * Math.Sin(angle)));
        }
        return points;
    }

    /// <summary>The closed profile turned around the vertical axis; the winding is corrected so the faces look outward.</summary>
    private static (List<Vector3>, List<int>) Revolve(IReadOnlyList<(double R, double Z)> profile, int segments = 64)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var ring = 0; ring < segments; ring++)
            foreach (var (r, z) in profile)
            {
                var angle = 2 * Math.PI * ring / segments;
                positions.Add(new Vector3((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)), (float)z));
            }
        var n = profile.Count;
        for (var ring = 0; ring < segments; ring++)
        {
            var next = (ring + 1) % segments;
            for (var k = 0; k < n; k++)
            {
                var l = (k + 1) % n;
                int a = ring * n + k, b = next * n + k, c = next * n + l, d = ring * n + l;
                AddIfSolid(positions, indices, a, b, c);
                AddIfSolid(positions, indices, a, c, d);
            }
        }
        // the cap and degenerate triangles (points on the axis) are dropped; the profile closes itself along the axis
        var volume = 0.0;
        for (var t = 0; t < indices.Count; t += 3)
            volume += Vector3.Dot(positions[indices[t]], Vector3.Cross(positions[indices[t + 1]], positions[indices[t + 2]])) / 6.0;
        if (volume < 0)
            for (var t = 0; t < indices.Count; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return (positions, indices);
    }

    private static void AddIfSolid(List<Vector3> positions, List<int> indices, int a, int b, int c)
    {
        if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() < 1e-12f) return;
        indices.Add(a); indices.Add(b); indices.Add(c);
    }
}

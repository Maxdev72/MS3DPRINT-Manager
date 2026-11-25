using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>A painted zone of a part: its colour and finish, and the triangles it covers (indices in the part as loaded).</summary>
public sealed record PaintZone(string Label, string Hex, string Finish, int[] Triangles);

/// <summary>
/// Picks what a click paints. « Element » follows the surface across every edge except the sharp inward ones (valleys), so a
/// foot, a button or a logo in relief is taken whole up to where it meets the body. « Face » stops at any sharp edge, so only
/// the clicked face and its smooth continuation are taken. Triangles are joined by their positions, so meshes whose triangles
/// do not share vertices (STL) work too.
/// </summary>
public sealed class PaintSelector
{
    private readonly MeshModel _mesh;
    private readonly List<int>[] _neighbours;
    private readonly List<(int A, int B)>[] _sharedEdge;

    public PaintSelector(MeshModel mesh)
    {
        _mesh = mesh;
        var count = mesh.TriangleCount;
        _neighbours = new List<int>[count];
        _sharedEdge = new List<(int, int)>[count];
        var scale = 1e4f / Math.Max(1e-6f, mesh.Size.Length());
        var ids = new Dictionary<(long, long, long), int>();
        int Id(int index)
        {
            var p = mesh.Positions[index] * scale;
            var key = ((long)MathF.Round(p.X), (long)MathF.Round(p.Y), (long)MathF.Round(p.Z));
            if (!ids.TryGetValue(key, out var id)) ids[key] = id = ids.Count;
            return id;
        }
        var edges = new Dictionary<(int, int), List<(int Triangle, int A, int B)>>();
        for (var t = 0; t < count; t++)
        {
            _neighbours[t] = [];
            _sharedEdge[t] = [];
            for (var e = 0; e < 3; e++)
            {
                var (a, b) = (mesh.Indices[t * 3 + e], mesh.Indices[t * 3 + (e + 1) % 3]);
                var (ia, ib) = (Id(a), Id(b));
                var key = ia < ib ? (ia, ib) : (ib, ia);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = [];
                list.Add((t, a, b));
            }
        }
        foreach (var list in edges.Values)
            for (var i = 0; i < list.Count; i++)
                for (var j = 0; j < list.Count; j++)
                    if (i != j)
                    {
                        _neighbours[list[i].Triangle].Add(list[j].Triangle);
                        _sharedEdge[list[i].Triangle].Add((list[i].A, list[i].B));
                    }
    }

    /// <summary>The triangles a click on <paramref name="triangle"/> paints; <paramref name="sharpDegrees"/> is the angle from which an edge is sharp.</summary>
    public HashSet<int> Select(int triangle, bool wholeElement, double sharpDegrees)
    {
        var sharp = (float)Math.Cos(sharpDegrees * Math.PI / 180);
        var chosen = new HashSet<int> { triangle };
        var queue = new Queue<int>([triangle]);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            for (var i = 0; i < _neighbours[t].Count; i++)
            {
                var n = _neighbours[t][i];
                if (chosen.Contains(n)) continue;
                var smooth = Vector3.Dot(_mesh.Normals[t], _mesh.Normals[n]) >= sharp;
                if (!smooth && (!wholeElement || IsValley(t, n, _sharedEdge[t][i]))) continue;
                chosen.Add(n);
                queue.Enqueue(n);
            }
        }
        return chosen;
    }

    /// <summary>An inward fold: the neighbour rises on the side the triangle faces (where a foot meets the body).</summary>
    private bool IsValley(int triangle, int neighbour, (int A, int B) edge)
    {
        var onEdge = (_mesh.Positions[edge.A] + _mesh.Positions[edge.B]) / 2;
        var far = Enumerable.Range(0, 3).Select(c => _mesh.Positions[_mesh.Indices[neighbour * 3 + c]])
            .MaxBy(p => Vector3.DistanceSquared(p, onEdge));
        return Vector3.Dot(_mesh.Normals[triangle], far - onEdge) > _mesh.Size.Length() * 1e-5f;
    }
}

public static partial class PaintParts
{
    /// <summary>
    /// Writes the part as STL files for Blender: the unpainted triangles, then one file per zone (a later zone wins a triangle
    /// painted twice). Returns the body file and the zones that cover something.
    /// </summary>
    public static (string Body, List<(PaintZone Zone, string File)> Zones) Write(MeshModel turned, IReadOnlyList<PaintZone> zones, string folder, string stem)
    {
        var owner = new int[turned.TriangleCount];
        for (var z = 0; z < zones.Count; z++)
            foreach (var t in zones[z].Triangles)
                if (t >= 0 && t < owner.Length) owner[t] = z + 1;
        MeshModel Subset(int zone)
        {
            var indices = new List<int>();
            for (var t = 0; t < owner.Length; t++)
                if (owner[t] == zone) indices.AddRange([turned.Indices[t * 3], turned.Indices[t * 3 + 1], turned.Indices[t * 3 + 2]]);
            return new MeshModel(turned.Positions, [.. indices]);
        }
        var body = Path.Combine(folder, stem + ".stl");
        StlWriter.Write(body, owner.Any(o => o == 0) ? Subset(0) : turned);
        var files = new List<(PaintZone, string)>();
        for (var z = 0; z < zones.Count; z++)
        {
            if (!owner.Contains(z + 1)) continue;
            var file = Path.Combine(folder, $"{stem}-zone{z + 1}.stl");
            StlWriter.Write(file, Subset(z + 1));
            files.Add((zones[z], file));
        }
        return (body, files);
    }
}

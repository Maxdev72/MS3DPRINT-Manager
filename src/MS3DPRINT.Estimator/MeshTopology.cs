using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>
/// What the connectivity of a mesh says about it: holes, edges shared by too many faces, flipped faces, and its closed surfaces.
/// Those that enclose material are <c>Shells</c> (separate pieces); those that enclose a void inside a piece are <c>Cavities</c>.
/// </summary>
internal readonly record struct TopologyReport(
    int BoundaryEdges, int NonManifoldEdges, int InconsistentEdges, int DegenerateTriangles, int Shells, int Cavities, double SignedVolume, double SurfaceArea)
{
    /// <summary>Every edge is shared by exactly two faces that agree on their direction: the mesh is a closed, consistently wound surface.</summary>
    public bool IsClean => BoundaryEdges == 0 && NonManifoldEdges == 0 && InconsistentEdges == 0;
}

internal static class MeshTopology
{
    /// <summary>Positions closer than this (in millimetres) are the same vertex: exporters round coordinates differently for neighbouring faces.</summary>
    private const double WeldStep = 1e-4;

    public static TopologyReport Analyse(PrintMesh mesh, CancellationToken cancellationToken)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        var triangleCount = mesh.TriangleCount;

        var ids = new Dictionary<(long, long, long), int>(positions.Length / 2 + 1);
        var vertexOf = new int[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var key = ((long)Math.Round(positions[i].X / WeldStep), (long)Math.Round(positions[i].Y / WeldStep), (long)Math.Round(positions[i].Z / WeldStep));
            if (!ids.TryGetValue(key, out var id)) { id = ids.Count; ids[key] = id; }
            vertexOf[i] = id;
        }

        var parent = new int[ids.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;
        var used = new bool[ids.Count];
        var edgeKeys = new List<ulong>(triangleCount * 3);
        var edgeForward = new List<bool>(triangleCount * 3);
        var degenerate = 0;
        double volume = 0, surface = 0;
        var triangleVolume = new double[triangleCount];
        var triangleVertex = new int[triangleCount];
        Array.Fill(triangleVertex, -1);
        var origin = (mesh.Min + mesh.Max) / 2;

        for (var t = 0; t < triangleCount; t++)
        {
            if ((t & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = positions[indices[t * 3]] - origin;
            var b = positions[indices[t * 3 + 1]] - origin;
            var c = positions[indices[t * 3 + 2]] - origin;
            var cross = Vector3.Cross(b - a, c - a);
            var tetrahedron = Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            volume += tetrahedron;
            var area = cross.Length() / 2.0;
            surface += area;

            int va = vertexOf[indices[t * 3]], vb = vertexOf[indices[t * 3 + 1]], vc = vertexOf[indices[t * 3 + 2]];
            if (va == vb || vb == vc || vc == va || area < 1e-9) { degenerate++; continue; }
            used[va] = used[vb] = used[vc] = true;
            triangleVolume[t] = tetrahedron;
            triangleVertex[t] = va;
            Union(parent, va, vb);
            Union(parent, vb, vc);
            AddEdge(edgeKeys, edgeForward, va, vb);
            AddEdge(edgeKeys, edgeForward, vb, vc);
            AddEdge(edgeKeys, edgeForward, vc, va);
        }

        var keys = edgeKeys.ToArray();
        var forward = edgeForward.ToArray();
        Array.Sort(keys, forward);
        int boundary = 0, nonManifold = 0, inconsistent = 0;
        for (var i = 0; i < keys.Length;)
        {
            var next = i;
            var forwardCount = 0;
            while (next < keys.Length && keys[next] == keys[i]) { if (forward[next]) forwardCount++; next++; }
            var count = next - i;
            if (count == 1) boundary++;
            else if (count > 2) nonManifold++;
            else if (forwardCount != 1) inconsistent++;
            i = next;
        }

        // a closed surface is a piece when it encloses volume the same way as the whole mesh, a cavity when it encloses it the other way
        var shellVolume = new Dictionary<int, double>();
        for (var t = 0; t < triangleCount; t++)
            if (triangleVertex[t] >= 0)
            {
                var root = Find(parent, triangleVertex[t]);
                shellVolume[root] = shellVolume.GetValueOrDefault(root) + triangleVolume[t];
            }
        var pieces = 0;
        var cavities = 0;
        foreach (var shell in shellVolume.Values)
        {
            if (volume == 0 || Math.Sign(shell) == Math.Sign(volume)) pieces++;
            else cavities++;
        }
        return new TopologyReport(boundary, nonManifold, inconsistent, degenerate, pieces, cavities, volume, surface);
    }

    private static void AddEdge(List<ulong> keys, List<bool> forward, int from, int to)
    {
        keys.Add(from < to ? ((ulong)(uint)from << 32) | (uint)to : ((ulong)(uint)to << 32) | (uint)from);
        forward.Add(from < to);
    }

    private static int Find(int[] parent, int v)
    {
        while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; }
        return v;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var ra = Find(parent, a);
        var rb = Find(parent, b);
        if (ra != rb) parent[ra] = rb;
    }
}

using System.Numerics;

namespace MS3DPRINT.Manager.App.Preview;

public readonly record struct WallTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Center => (A + B + C) / 3;
    public Vector3 Normal => Vector3.Normalize(Vector3.Cross(B - A, C - A));
}
public sealed record ThinWallSample(int TriangleIndex, Vector3 Position, double ThicknessMm);
public sealed record WallAnalysisResult(int TriangleCount, int SampleCount, int BoundaryEdges,
    int NonManifoldEdges, int InconsistentEdges, int InvalidTriangles, long IntersectionTests,
    bool BudgetExhausted, IReadOnlyList<ThinWallSample> ThinSamples);

/// <summary>Advisory centroid/normal rays, not a minimum-thickness or printability proof.
/// Exact coordinate welding; self-intersections and vertex-only manifold defects are not checked.</summary>
public static class WallThicknessAnalyzer
{
    private const int MaxSamples = 2048;
    private const long WorkLimit = 2_000_000;

    public static WallAnalysisResult Analyze(IReadOnlyList<WallTriangle> input, double millimetersPerUnit,
        double minimumThicknessMm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0 ||
            !double.IsFinite(minimumThicknessMm) || minimumThicknessMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumThicknessMm));
        if (input.Count > 2_000_000) throw new ArgumentException("Analyse limitée à 2 millions de triangles.");
        var vertices = new Dictionary<Vector3, int>();
        var edges = new Dictionary<(int, int), (int Count, int Direction)>();
        var valid = new List<int>(input.Count);
        int invalid = 0;
        for (int i = 0; i < input.Count; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var t = input[i];
            if (!Finite(t.A) || !Finite(t.B) || !Finite(t.C) || !Finite(t.Normal)) { invalid++; continue; }
            valid.Add(i);
            var a = Vertex(t.A); var b = Vertex(t.B); var c = Vertex(t.C);
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        int boundary = edges.Values.Count(e => e.Count == 1);
        int nonmanifold = edges.Values.Count(e => e.Count > 2);
        int inconsistent = edges.Values.Count(e => e.Count == 2 && e.Direction != 0);
        // Parity is meaningful only for a closed, consistently oriented triangle surface.
        if (boundary != 0 || nonmanifold != 0 || inconsistent != 0 || invalid != 0)
            return new(input.Count, 0, boundary, nonmanifold, inconsistent, invalid, 0, false, []);
        var ids = valid.ToArray();
        var root = ids.Length == 0 ? null : Build(0, ids.Length);
        var samples = new List<ThinWallSample>();
        int count = Math.Min(MaxSamples, valid.Count), completed = 0;
        long work = 0;
        double maximum = minimumThicknessMm / millimetersPerUnit;
        for (int s = 0; s < count && work < WorkLimit; s++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = valid[(int)((long)s * valid.Count / count)];
            var t = input[index];
            bool thin = false;
            float closestThin = float.MaxValue;
            foreach (var direction in new[] { t.Normal, -t.Normal })
            {
                float closest = (float)Math.Min(maximum, float.MaxValue);
                bool hit = false;
                Trace(root!, t.Center, direction, index, ref closest, ref hit);
                if (!hit || work >= WorkLimit) continue;
                // An odd number of forward surface crossings means this ray enters solid
                // material. An even number means an air gap / exterior path.
                if (MaterialCrossings(root!, t.Center, direction, index) % 2 == 1 && work < WorkLimit)
                {
                    thin = true;
                    closestThin = Math.Min(closestThin, closest);
                }
            }
            if (work >= WorkLimit) break;
            completed++;
            if (thin && closestThin * millimetersPerUnit < minimumThicknessMm)
                samples.Add(new(index, t.Center, closestThin * millimetersPerUnit));
        }
        return new(input.Count, completed, boundary, nonmanifold, inconsistent, invalid, work, work >= WorkLimit, samples);

        int Vertex(Vector3 v) { if (!vertices.TryGetValue(v, out var id)) vertices.Add(v, id = vertices.Count); return id; }
        void Edge(int a, int b)
        {
            var key = a < b ? (a,b) : (b,a);
            edges.TryGetValue(key, out var e);
            edges[key] = (e.Count + 1, e.Direction + (a < b ? 1 : -1));
        }
        Node Build(int start, int length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
            for (int j = start; j < start + length; j++)
            {
                if ((j & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var t = input[ids[j]];
                min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B,t.C)));
                max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B,t.C)));
            }
            var node = new Node(min, max, start, length);
            if (length <= 8) return node;
            var size = max - min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            int comparisons = 0;
            Array.Sort(ids, start, length, Comparer<int>.Create((a,b) =>
            {
                if ((++comparisons & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
                return Component(input[a].Center,axis).CompareTo(Component(input[b].Center,axis));
            }));
            int half = length / 2;
            node.Left = Build(start, half); node.Right = Build(start + half, length - half);
            return node;
        }
        void Trace(Node node, Vector3 origin, Vector3 direction, int source, ref float closest, ref bool hit)
        {
            if (work >= WorkLimit) return;
            work++;
            if ((work & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!IntersectsBox(node, origin, direction, closest)) return;
            if (node.Left is not null)
            {
                Trace(node.Left, origin, direction, source, ref closest, ref hit);
                Trace(node.Right!, origin, direction, source, ref closest, ref hit);
                return;
            }
            for (int j = node.Start; j < node.Start + node.Length && work < WorkLimit; j++)
            {
                work++;
                int index = ids[j]; if (index == source) continue;
                var t = input[index];
                // Opposing surfaces only; either winding direction can be globally reversed.
                if (Vector3.Dot(input[source].Normal, t.Normal) > -0.25f) continue;
                if (!RayDistance(t, origin, direction, out float distance)) continue;
                if (distance > 1e-7f && distance < closest) { closest = distance; hit = true; }
            }
        }
        int MaterialCrossings(Node node, Vector3 origin, Vector3 direction, int source)
        {
            var distances = new List<float>();
            Visit(node);
            return distances.Count;

            void Visit(Node current)
            {
                if (work >= WorkLimit) return;
                work++;
                if ((work & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!IntersectsBox(current, origin, direction, float.MaxValue)) return;
                if (current.Left is not null)
                {
                    Visit(current.Left);
                    Visit(current.Right!);
                    return;
                }
                for (int j = current.Start; j < current.Start + current.Length && work < WorkLimit; j++)
                {
                    work++;
                    int index = ids[j];
                    if (index == source || !RayDistance(input[index], origin, direction, out float distance) || distance <= 1e-7f) continue;
                    // Adjacent coplanar triangles can share one ray crossing.
                    if (distances.Any(d => Math.Abs(d - distance) <= Math.Max(1e-5f, distance * 1e-6f))) continue;
                    distances.Add(distance);
                }
            }
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    private static bool RayDistance(WallTriangle t, Vector3 origin, Vector3 direction, out float distance)
    {
        distance = 0;
        var e1 = t.B - t.A; var e2 = t.C - t.A;
        var p = Vector3.Cross(direction, e2); var det = Vector3.Dot(e1, p);
        if (Math.Abs(det) < 1e-20f) return false;
        var delta = origin - t.A; var u = Vector3.Dot(delta, p) / det;
        if (u < -1e-5 || u > 1.00001) return false;
        var q = Vector3.Cross(delta, e1); var v = Vector3.Dot(direction, q) / det;
        if (v < -1e-5 || u + v > 1.00001) return false;
        distance = Vector3.Dot(e2, q) / det;
        return true;
    }
    private static bool IntersectsBox(Node node, Vector3 origin, Vector3 direction, float maximum)
    {
        float near = 0, far = maximum;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = Component(origin,axis), d = Component(direction,axis), min = Component(node.Min,axis), max = Component(node.Max,axis);
            if (Math.Abs(d) < 1e-20) { if (o < min || o > max) return false; continue; }
            float a = (min-o)/d, b = (max-o)/d;
            near = Math.Max(near, Math.Min(a,b)); far = Math.Min(far, Math.Max(a,b));
            if (near > far) return false;
        }
        return true;
    }
    private sealed class Node(Vector3 min, Vector3 max, int start, int length)
    {
        public Vector3 Min = min, Max = max;
        public int Start = start, Length = length;
        public Node? Left, Right;
    }
}

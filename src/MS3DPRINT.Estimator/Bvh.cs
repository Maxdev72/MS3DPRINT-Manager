using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>Bounding-volume hierarchy over the triangles of a mesh, to find the first surface a ray meets.</summary>
internal sealed class Bvh
{
    private const int LeafSize = 4;
    private const int StackSize = 96;

    private readonly Vector3[] _positions;
    private readonly int[] _indices;
    private readonly int[] _order;
    private readonly Vector3[] _min, _max;
    private readonly int[] _left, _right, _first, _count;
    private int _nodes;

    public Bvh(PrintMesh mesh, CancellationToken cancellationToken)
    {
        _positions = mesh.Positions;
        _indices = mesh.Indices;
        var triangles = mesh.TriangleCount;
        _order = new int[triangles];
        var centroids = new Vector3[triangles];
        for (var t = 0; t < triangles; t++)
        {
            _order[t] = t;
            centroids[t] = (_positions[_indices[t * 3]] + _positions[_indices[t * 3 + 1]] + _positions[_indices[t * 3 + 2]]) / 3;
        }

        var capacity = Math.Max(1, 2 * triangles);
        _min = new Vector3[capacity];
        _max = new Vector3[capacity];
        _left = new int[capacity];
        _right = new int[capacity];
        _first = new int[capacity];
        _count = new int[capacity];

        var pending = new Stack<(int Node, int Start, int End)>();
        pending.Push((_nodes++, 0, triangles));
        var keys = new float[triangles];
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, start, end) = pending.Pop();
            Vector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity), centreLow = low, centreHigh = high;
            for (var i = start; i < end; i++)
            {
                var t = _order[i];
                for (var corner = 0; corner < 3; corner++)
                {
                    var point = _positions[_indices[t * 3 + corner]];
                    low = Vector3.Min(low, point);
                    high = Vector3.Max(high, point);
                }
                centreLow = Vector3.Min(centreLow, centroids[t]);
                centreHigh = Vector3.Max(centreHigh, centroids[t]);
            }
            _min[node] = low;
            _max[node] = high;

            var spread = centreHigh - centreLow;
            if (end - start <= LeafSize || (spread.X <= 0 && spread.Y <= 0 && spread.Z <= 0))
            {
                _left[node] = -1;
                _first[node] = start;
                _count[node] = end - start;
                continue;
            }

            var axis = spread.X >= spread.Y && spread.X >= spread.Z ? 0 : spread.Y >= spread.Z ? 1 : 2;
            for (var i = start; i < end; i++) keys[i] = axis == 0 ? centroids[_order[i]].X : axis == 1 ? centroids[_order[i]].Y : centroids[_order[i]].Z;
            Array.Sort(keys, _order, start, end - start);
            var middle = (start + end) / 2;
            _left[node] = _nodes++;
            _right[node] = _nodes++;
            pending.Push((_left[node], start, middle));
            pending.Push((_right[node], middle, end));
        }
    }

    /// <summary>Nearest triangle hit by the ray (Möller–Trumbore), skipping one triangle; false when it meets nothing.</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, int ignore, out float distance, out int triangle)
    {
        distance = float.PositiveInfinity;
        triangle = -1;
        var inverse = new Vector3(1 / direction.X, 1 / direction.Y, 1 / direction.Z);
        Span<int> stack = stackalloc int[StackSize];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = stack[--top];
            if (!HitsBox(origin, inverse, _min[node], _max[node], distance)) continue;
            if (_left[node] < 0)
            {
                for (var i = _first[node]; i < _first[node] + _count[node]; i++)
                {
                    var t = _order[i];
                    if (t == ignore) continue;
                    if (HitsTriangle(origin, direction, t, out var d) && d < distance) { distance = d; triangle = t; }
                }
            }
            else if (top + 2 <= StackSize)
            {
                stack[top++] = _left[node];
                stack[top++] = _right[node];
            }
        }
        return triangle >= 0;
    }

    private static bool HitsBox(Vector3 origin, Vector3 inverse, Vector3 low, Vector3 high, float limit)
    {
        var t1 = (low - origin) * inverse;
        var t2 = (high - origin) * inverse;
        var near = Vector3.Min(t1, t2);
        var far = Vector3.Max(t1, t2);
        var enter = Math.Max(Math.Max(near.X, near.Y), near.Z);
        var leave = Math.Min(Math.Min(far.X, far.Y), far.Z);
        return leave >= Math.Max(enter, 0) && enter <= limit;
    }

    private bool HitsTriangle(Vector3 origin, Vector3 direction, int t, out float distance)
    {
        distance = 0;
        var a = _positions[_indices[t * 3]];
        var edge1 = _positions[_indices[t * 3 + 1]] - a;
        var edge2 = _positions[_indices[t * 3 + 2]] - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (Math.Abs(determinant) < 1e-12f) return false;
        var inverseDeterminant = 1 / determinant;
        var offset = origin - a;
        var u = Vector3.Dot(offset, p) * inverseDeterminant;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(offset, edge1);
        var v = Vector3.Dot(direction, q) * inverseDeterminant;
        if (v < 0 || u + v > 1) return false;
        distance = Vector3.Dot(edge2, q) * inverseDeterminant;
        return distance > 1e-6f;
    }
}

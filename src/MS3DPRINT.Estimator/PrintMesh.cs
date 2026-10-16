using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>Triangle mesh in millimetres, three indices per triangle, counter-clockwise seen from outside.</summary>
public sealed class PrintMesh
{
    public PrintMesh(Vector3[] positions, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0) throw new ArgumentException("Le nombre d'indices doit être un multiple de 3.", nameof(indices));
        foreach (var index in indices)
            if ((uint)index >= (uint)positions.Length) throw new ArgumentException("Un indice dépasse le nombre de sommets.", nameof(indices));

        Positions = positions;
        Indices = indices;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var point in positions)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) throw new ArgumentException("Le maillage contient une coordonnée non finie.", nameof(positions));
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        Min = positions.Length == 0 ? Vector3.Zero : min;
        Max = positions.Length == 0 ? Vector3.Zero : max;
    }

    public Vector3[] Positions { get; }
    public int[] Indices { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public int TriangleCount => Indices.Length / 3;
    public Vector3 Size => Max - Min;

    /// <summary>
    /// The mesh turned about the centre of its bounding box, then stood on the bed: centred on the origin in X and Y and its lowest
    /// point at Z = 0. The triangles are unchanged and the mesh itself is not modified.
    /// </summary>
    public PrintMesh Oriented(Quaternion rotation)
    {
        if (Positions.Length == 0) return this;
        var centre = (Min + Max) / 2;
        var turned = new Vector3[Positions.Length];
        var low = new Vector3(float.PositiveInfinity);
        var high = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < turned.Length; i++)
        {
            var point = Vector3.Transform(Positions[i] - centre, rotation);
            turned[i] = point;
            low = Vector3.Min(low, point);
            high = Vector3.Max(high, point);
        }
        var shift = new Vector3(-(low.X + high.X) / 2, -(low.Y + high.Y) / 2, -low.Z);
        for (var i = 0; i < turned.Length; i++) turned[i] += shift;
        return new PrintMesh(turned, Indices);
    }

    /// <summary>The same mesh with every coordinate multiplied, to bring a model drawn in inches or metres to millimetres.</summary>
    public PrintMesh Scaled(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var scaled = new Vector3[Positions.Length];
        for (var i = 0; i < scaled.Length; i++) scaled[i] = Positions[i] * factor;
        return new PrintMesh(scaled, Indices);
    }
}

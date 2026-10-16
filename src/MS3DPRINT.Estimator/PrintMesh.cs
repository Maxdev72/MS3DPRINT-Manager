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

    /// <summary>The same mesh with every coordinate multiplied, to bring a model drawn in inches or metres to millimetres.</summary>
    public PrintMesh Scaled(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var scaled = new Vector3[Positions.Length];
        for (var i = 0; i < scaled.Length; i++) scaled[i] = Positions[i] * factor;
        return new PrintMesh(scaled, Indices);
    }
}

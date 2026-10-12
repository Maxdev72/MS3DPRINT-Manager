using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Triangle mesh shared by the loader, the renderer and the wall analysis.</summary>
public sealed class MeshModel
{
    public MeshModel(Vector3[] positions, int[] indices)
    {
        Positions = positions;
        Indices = indices;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var point in positions)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        Min = min;
        Max = max;
        Normals = BuildNormals(positions, indices);
    }

    public Vector3[] Positions { get; }
    public int[] Indices { get; }
    /// <summary>Unit face normals, one per triangle (zero vector for degenerate triangles).</summary>
    public Vector3[] Normals { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public int TriangleCount => Indices.Length / 3;
    public Vector3 Size => Max - Min;
    public Vector3 Center => (Min + Max) / 2;
    /// <summary>Half of the bounding-box diagonal, never zero.</summary>
    public float Radius => Size.Length() is var length && length > 0 && float.IsFinite(length) ? length / 2 : 1;

    private static Vector3[] BuildNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[indices.Length / 3];
        for (var triangle = 0; triangle < normals.Length; triangle++)
        {
            var a = positions[indices[triangle * 3]];
            var b = positions[indices[triangle * 3 + 1]];
            var c = positions[indices[triangle * 3 + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            normals[triangle] = length > 0 && float.IsFinite(length) ? normal / length : Vector3.Zero;
        }
        return normals;
    }
}

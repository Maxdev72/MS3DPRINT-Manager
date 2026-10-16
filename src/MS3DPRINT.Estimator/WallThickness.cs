using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>Local thickness measured under one face: where it is, how thick the material is there, and how much surface it stands for.</summary>
internal readonly record struct ThicknessSample(Vector3 Position, float ThicknessMm, double AreaMm2);

internal static class WallThickness
{
    /// <summary>
    /// From the middle of faces, a ray goes inward until it leaves the material through the opposite surface: the distance is
    /// the thickness there. When there are more faces than <paramref name="maxSamples"/>, evenly spread ones are taken and each
    /// stands for the surface of those it replaces. The mesh must be closed and wound outward.
    /// </summary>
    public static List<ThicknessSample> Measure(PrintMesh mesh, int maxSamples, CancellationToken cancellationToken)
    {
        var samples = new List<ThicknessSample>();
        var triangles = mesh.TriangleCount;
        if (triangles == 0) return samples;
        var bvh = new Bvh(mesh, cancellationToken);
        var stride = Math.Max(1, triangles / Math.Max(1, maxSamples));
        for (var t = 0; t < triangles; t += stride)
        {
            if ((t / stride & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            var length = cross.Length();
            if (!(length > 1e-9f)) continue;
            var normal = cross / length;
            var centre = (a + b + c) / 3;
            var inward = -normal;
            // a ray leaving the material through a back face is a thickness; one meeting a front face has crossed a void
            if (bvh.Raycast(centre + inward * 1e-3f, inward, t, out var distance, out var hit)
                && TriangleNormalDot(mesh, hit, inward) > 0)
                samples.Add(new ThicknessSample(centre, distance + 1e-3f, length / 2.0 * stride));
        }
        return samples;
    }

    private static float TriangleNormalDot(PrintMesh mesh, int triangle, Vector3 direction)
    {
        var a = mesh.Positions[mesh.Indices[triangle * 3]];
        var b = mesh.Positions[mesh.Indices[triangle * 3 + 1]];
        var c = mesh.Positions[mesh.Indices[triangle * 3 + 2]];
        return Vector3.Dot(Vector3.Cross(b - a, c - a), direction);
    }
}

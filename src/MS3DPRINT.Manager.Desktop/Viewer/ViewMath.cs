using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Camera geometry shared by picking and overlays: the same model the renderers use, for one point at a time.</summary>
public static class ViewMath
{
    private const double FieldOfView = 40 * Math.PI / 180;

    private static Matrix4x4 Rotation(ViewCamera camera) =>
        Matrix4x4.CreateRotationZ((float)camera.Yaw) * Matrix4x4.CreateRotationX((float)camera.Pitch);

    private static double Distance(MeshModel mesh, ViewCamera camera) => mesh.Radius * 2.9 / Math.Max(ViewCamera.MinZoom, camera.Zoom);

    /// <summary>Screen position (x right, y down, in the units of <paramref name="width"/> and <paramref name="height"/>) of a model point.</summary>
    public static bool TryProject(MeshModel mesh, ViewCamera camera, double width, double height, Vector3 point, out double x, out double y)
    {
        var view = Vector3.Transform(point - mesh.Center, Rotation(camera));
        var depth = view.Y + Distance(mesh, camera);
        if (depth <= 1e-6) { x = y = 0; return false; }
        var focal = height * 0.5 / Math.Tan(FieldOfView / 2);
        var scale = height / 760.0;
        x = width / 2 + camera.PanX * scale + view.X * focal / depth;
        y = height / 2 + camera.PanY * scale - view.Z * focal / depth;
        return true;
    }

    /// <summary>Model-space ray through a screen position.</summary>
    public static (Vector3 Origin, Vector3 Direction) Ray(MeshModel mesh, ViewCamera camera, double width, double height, double screenX, double screenY)
    {
        var focal = height * 0.5 / Math.Tan(FieldOfView / 2);
        var scale = height / 760.0;
        var cx = width / 2 + camera.PanX * scale;
        var cy = height / 2 + camera.PanY * scale;
        var viewDirection = new Vector3((float)((screenX - cx) / focal), 1, (float)(-(screenY - cy) / focal));
        var toModel = Matrix4x4.Transpose(Rotation(camera));
        var direction = Vector3.Normalize(Vector3.Transform(viewDirection, toModel));
        var origin = mesh.Center + Vector3.Transform(new Vector3(0, (float)-Distance(mesh, camera), 0), toModel);
        return (origin, direction);
    }
}

/// <summary>Finds where a ray meets the mesh (front-most triangle, either side).</summary>
public static class MeshPicker
{
    public static bool TryPick(MeshModel mesh, Vector3 origin, Vector3 direction, out Vector3 hit, out int triangle, ClipPlane? clip = null)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        var count = mesh.TriangleCount;
        var bestT = float.PositiveInfinity;
        var bestTriangle = -1;
        var gate = new object();
        Parallel.For(0, (count + 8191) / 8192, block =>
        {
            var localT = float.PositiveInfinity;
            var localTriangle = -1;
            var end = Math.Min(count, (block + 1) * 8192);
            for (var t = block * 8192; t < end; t++)
            {
                var a = positions[indices[t * 3]];
                if (clip is { } plane && plane.Clips((a + positions[indices[t * 3 + 1]] + positions[indices[t * 3 + 2]]) / 3f)) continue;
                var edge1 = positions[indices[t * 3 + 1]] - a;
                var edge2 = positions[indices[t * 3 + 2]] - a;
                var p = Vector3.Cross(direction, edge2);
                var determinant = Vector3.Dot(edge1, p);
                if (MathF.Abs(determinant) < 1e-12f) continue;
                var inverse = 1 / determinant;
                var s = origin - a;
                var u = Vector3.Dot(s, p) * inverse;
                if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, edge1);
                var v = Vector3.Dot(direction, q) * inverse;
                if (v < 0 || u + v > 1) continue;
                var distance = Vector3.Dot(edge2, q) * inverse;
                if (distance > 1e-6f && distance < localT) { localT = distance; localTriangle = t; }
            }
            if (localTriangle < 0) return;
            lock (gate)
            {
                if (localT < bestT) { bestT = localT; bestTriangle = localTriangle; }
            }
        });
        triangle = bestTriangle;
        hit = bestTriangle < 0 ? default : origin + direction * bestT;
        return bestTriangle >= 0;
    }
}

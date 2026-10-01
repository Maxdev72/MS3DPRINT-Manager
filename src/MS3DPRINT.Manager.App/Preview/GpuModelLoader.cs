using System.Numerics;
using System.Windows.Media.Media3D;
using HelixToolkit;
using HelixToolkit.Wpf;
using HelixToolkit.SharpDX;
using MS3DPRINT.Manager.Core.Files;
using GpuMesh = HelixToolkit.SharpDX.MeshGeometry3D;
using WpfMesh = System.Windows.Media.Media3D.MeshGeometry3D;

namespace MS3DPRINT.Manager.App.Preview;

public static class GpuModelLoader
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);

    public static GpuPreviewScene Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ThreeDFileSupport.IsPreviewable(path))
            throw new NotSupportedException("Seuls les fichiers STL et OBJ sont pris en charge.");

        ImportGate.Wait(cancellationToken);
        try { return LoadCore(path, cancellationToken); }
        finally { ImportGate.Release(); }
    }

    private static GpuPreviewScene LoadCore(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryLoadBinaryStl(path, cancellationToken, out var binaryScene)) return binaryScene;

        var source = new ModelImporter().Load(path)
            ?? throw new InvalidDataException("Le modèle 3D ne contient aucune géométrie affichable.");
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = source.Bounds;
        var parts = new List<GpuMesh>();
        AddMeshes(source, Transform3D.Identity, parts, cancellationToken);
        var triangles = parts.Sum(mesh => (mesh.TriangleIndices?.Count ?? 0) / 3);
        if (bounds.IsEmpty || triangles == 0)
            throw new InvalidDataException("Le modèle 3D ne contient aucune géométrie affichable.");
        return new GpuPreviewScene(parts, bounds, triangles);
    }

    private static bool TryLoadBinaryStl(string path, CancellationToken cancellationToken, out GpuPreviewScene scene)
    {
        scene = null!;
        if (!Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < 134) return false;
        stream.Position = 80;
        using (var header = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var count = header.ReadUInt32();
            if (count == 0 || stream.Length != 84L + 50L * count) return false;
        }
        stream.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // The pinned 3.1.2 reader avoids a second full-mesh copy for binary STL.
        var objects = new HelixToolkit.SharpDX.StLReader().Read(stream);
#pragma warning restore CS0618
        cancellationToken.ThrowIfCancellationRequested();
        if (objects is null) return false;
        var parts = objects.Select(item => item.Geometry).OfType<GpuMesh>().Where(mesh => mesh.Positions is { Count: > 0 }).ToList();
        if (parts.Count == 0) return false;
        var minimumX = double.PositiveInfinity;
        var minimumY = double.PositiveInfinity;
        var minimumZ = double.PositiveInfinity;
        var maximumX = double.NegativeInfinity;
        var maximumY = double.NegativeInfinity;
        var maximumZ = double.NegativeInfinity;
        foreach (var part in parts)
        {
            var positions = part.Positions!;
            for (var index = 0; index < positions.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var point = positions[index];
                minimumX = Math.Min(minimumX, point.X);
                minimumY = Math.Min(minimumY, point.Y);
                minimumZ = Math.Min(minimumZ, point.Z);
                maximumX = Math.Max(maximumX, point.X);
                maximumY = Math.Max(maximumY, point.Y);
                maximumZ = Math.Max(maximumZ, point.Z);
            }
        }
        var triangles = parts.Sum(mesh => (mesh.TriangleIndices?.Count ?? 0) / 3);
        if (triangles == 0) return false;
        var bounds = new Rect3D(minimumX, minimumY, minimumZ,
            maximumX - minimumX, maximumY - minimumY, maximumZ - minimumZ);
        scene = new GpuPreviewScene(parts, bounds, triangles);
        return true;
    }

    private static void AddMeshes(Model3D model, Transform3D parent, List<GpuMesh> parts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transform = new MatrixTransform3D(model.Transform.Value * parent.Value);
        if (model is Model3DGroup group)
        {
            foreach (var child in group.Children) AddMeshes(child, transform, parts, cancellationToken);
            return;
        }

        if (model is not GeometryModel3D { Geometry: WpfMesh source }) return;
        var positions = new Vector3Collection(source.Positions.Count);
        for (var index = 0; index < source.Positions.Count; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var point = source.Positions[index];
            var converted = transform.Transform(point);
            positions.Add(new Vector3((float)converted.X, (float)converted.Y, (float)converted.Z));
        }

        var indices = new IntCollection(source.TriangleIndices.Count);
        for (var index = 0; index < source.TriangleIndices.Count; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            indices.Add(source.TriangleIndices[index]);
        }
        var mesh = new GpuMesh { Positions = positions, TriangleIndices = indices };
        var accumulated = new Vector3[positions.Count];
        for (var index = 0; index + 2 < indices.Count; index += 3)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = indices[index];
            var b = indices[index + 1];
            var c = indices[index + 2];
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            accumulated[a] += normal;
            accumulated[b] += normal;
            accumulated[c] += normal;
        }
        var normals = new Vector3Collection(accumulated.Length);
        foreach (var normal in accumulated)
            normals.Add(normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ);
        mesh.Normals = normals;
        parts.Add(mesh);
    }
}

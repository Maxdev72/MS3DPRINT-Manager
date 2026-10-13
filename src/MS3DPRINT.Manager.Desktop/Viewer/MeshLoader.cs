using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Reads STL (binary or ASCII) and OBJ files into a <see cref="MeshModel"/> without any native dependency.</summary>
public static class MeshLoader
{
    /// <summary>Above this size the whole file would have to be held in memory (more for ASCII STL): it is refused with a clear message.</summary>
    public const long MaxFileBytes = 400L * 1024 * 1024;

    private const string EmptyMessage = "Le modèle 3D ne contient aucune géométrie affichable.";

    public static MeshModel Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ThreeDFileSupport.IsPreviewable(path))
            throw new NotSupportedException("Seuls les fichiers STL et OBJ sont pris en charge.");
        if (new FileInfo(path) is { Exists: true } info && info.Length > MaxFileBytes)
            throw new InvalidDataException($"Ce fichier fait {info.Length / (1024 * 1024)} Mo : au-delà de {MaxFileBytes / (1024 * 1024)} Mo, il est trop volumineux pour être affiché.");

        var mesh = Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase)
            ? LoadObj(path, cancellationToken)
            : LoadStl(path, cancellationToken);
        if (mesh.TriangleCount == 0 || !float.IsFinite(mesh.Size.X + mesh.Size.Y + mesh.Size.Z))
            throw new InvalidDataException(EmptyMessage);
        return mesh;
    }

    private static MeshModel LoadStl(string path, CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 84)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80, 4));
            if (count > 0 && bytes.LongLength == 84L + 50L * count)
                return LoadBinaryStl(bytes, (int)count, cancellationToken);
        }
        return LoadAsciiStl(System.Text.Encoding.ASCII.GetString(bytes), cancellationToken);
    }

    private static MeshModel LoadBinaryStl(byte[] bytes, int count, CancellationToken cancellationToken)
    {
        var positions = new Vector3[count * 3];
        var indices = new int[count * 3];
        for (var triangle = 0; triangle < count; triangle++)
        {
            if ((triangle & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = 84 + triangle * 50 + 12; // skip the stored normal, it is recomputed
            for (var corner = 0; corner < 3; corner++)
            {
                var at = offset + corner * 12;
                positions[triangle * 3 + corner] = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 4, 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8, 4)));
                indices[triangle * 3 + corner] = triangle * 3 + corner;
            }
        }
        return new MeshModel(positions, indices);
    }

    private static MeshModel LoadAsciiStl(string text, CancellationToken cancellationToken)
    {
        var positions = new List<Vector3>();
        var line = 0;
        foreach (var raw in text.Split('\n'))
        {
            if ((++line & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var trimmed = raw.AsSpan().Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed[6..].ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) throw new InvalidDataException("Fichier STL invalide : sommet incomplet.");
            positions.Add(new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2])));
        }
        var usable = positions.Count / 3 * 3;
        if (usable == 0) throw new InvalidDataException(EmptyMessage);
        var array = positions.Take(usable).ToArray();
        return new MeshModel(array, Enumerable.Range(0, usable).ToArray());
    }

    private static MeshModel LoadObj(string path, CancellationToken cancellationToken)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var line = 0;
        foreach (var raw in File.ReadLines(path))
        {
            if ((++line & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("v ", StringComparison.Ordinal))
            {
                var parts = trimmed[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) throw new InvalidDataException("Fichier OBJ invalide : sommet incomplet.");
                positions.Add(new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2])));
            }
            else if (trimmed.StartsWith("f ", StringComparison.Ordinal))
            {
                var corners = trimmed[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(token => ResolveIndex(token, positions.Count)).ToArray();
                for (var corner = 1; corner + 1 < corners.Length; corner++)
                {
                    indices.Add(corners[0]);
                    indices.Add(corners[corner]);
                    indices.Add(corners[corner + 1]);
                }
            }
        }
        if (positions.Count == 0 || indices.Count == 0) throw new InvalidDataException(EmptyMessage);
        return new MeshModel(positions.ToArray(), indices.ToArray());
    }

    private static int ResolveIndex(string token, int vertexCount)
    {
        var slash = token.IndexOf('/');
        var text = slash >= 0 ? token[..slash] : token;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value == 0)
            throw new InvalidDataException("Fichier OBJ invalide : indice de face incorrect.");
        var index = value > 0 ? value - 1 : vertexCount + value;
        if (index < 0 || index >= vertexCount)
            throw new InvalidDataException("Fichier OBJ invalide : face qui référence un sommet inexistant.");
        return index;
    }

    private static float ParseFloat(string text) =>
        float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

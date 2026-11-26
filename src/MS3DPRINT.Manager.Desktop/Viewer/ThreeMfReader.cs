using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Xml;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Reads the geometry of a 3MF file (a ZIP of XML models, as written by Bambu Studio, OrcaSlicer, PrusaSlicer or CAD software)
/// into one mesh in millimetres: every object of the build plate with its position, and the objects that are assembled from
/// components (possibly kept in other model files of the archive).
/// </summary>
public static class ThreeMfReader
{
    private const string ProductionNamespace = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private const string RelationshipsPath = "_rels/.rels";
    private const string DefaultModelPath = "3D/3dmodel.model";
    /// <summary>What the XML of all the models may weigh once unpacked: a small ZIP can hide gigabytes.</summary>
    private const long MaxUnpackedBytes = 800L * 1024 * 1024;
    private const int MaxTriangles = 25_000_000;
    private const int MaxDepth = 16;

    private sealed record Component(string? Path, int ObjectId, Matrix4x4 Transform);

    private sealed class ModelObject
    {
        public string? Type { get; init; }
        public string? Name { get; init; }
        public List<Vector3> Vertices { get; } = [];
        public List<int> Triangles { get; } = [];
        public List<Component> Components { get; } = [];
        public bool HasMesh => Triangles.Count > 0;
    }

    private sealed class ModelPart
    {
        public double MillimetresPerUnit { get; set; } = 1;
        public Dictionary<int, ModelObject> Objects { get; } = [];
        public List<(int ObjectId, Matrix4x4 Transform)> Build { get; } = [];
    }

    public static MeshModel Load(string path, CancellationToken cancellationToken = default)
    {
        var objects = LoadObjects(path, cancellationToken);
        if (objects.Count == 1) return objects[0].Mesh;
        var positions = new List<Vector3>();
        var indices = new List<int>();
        foreach (var (_, mesh) in objects)
        {
            var first = positions.Count;
            positions.AddRange(mesh.Positions);
            indices.AddRange(mesh.Indices.Select(index => first + index));
        }
        return new MeshModel([.. positions], [.. indices]);
    }

    /// <summary>The objects of the build plate one by one (name, mesh in millimetres, in place), for a scene that separates them.</summary>
    public static IReadOnlyList<(string Name, MeshModel Mesh)> LoadObjects(string path, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(path);
        var unpacked = archive.Entries.Where(entry => entry.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.Length);
        if (unpacked > MaxUnpackedBytes) throw new InvalidDataException("Ce fichier 3MF est trop volumineux une fois décompressé pour être affiché.");

        var parts = new Dictionary<string, ModelPart>(StringComparer.OrdinalIgnoreCase);
        ModelPart Part(string partPath)
        {
            var key = Normalise(partPath);
            if (parts.TryGetValue(key, out var known)) return known;
            var entry = Find(archive, key) ?? throw new InvalidDataException("Fichier 3MF incomplet : le modèle « " + key + " » est introuvable.");
            return parts[key] = ReadPart(entry, cancellationToken);
        }

        var rootPath = RootModelPath(archive);
        var root = Part(rootPath);
        var objects = new List<(string, MeshModel)>();
        var triangles = 0;
        List<Vector3> positions = [];
        List<int> indices = [];

        void Emit(string partPath, int objectId, Matrix4x4 transform, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaxDepth) throw new InvalidDataException("Fichier 3MF invalide : assemblage trop profond ou circulaire.");
            var part = Part(partPath);
            if (!part.Objects.TryGetValue(objectId, out var model)) return;
            // supports are part of the print job, not of the object
            if (model.Type is "support" or "solidsupport") return;
            if (model.HasMesh)
            {
                var first = positions.Count;
                foreach (var vertex in model.Vertices) positions.Add(Vector3.Transform(vertex, transform));
                foreach (var index in model.Triangles) indices.Add(first + index);
            }
            foreach (var component in model.Components)
                Emit(component.Path ?? partPath, component.ObjectId, component.Transform * transform, depth + 1);
        }

        void Item(int objectId, Matrix4x4 transform)
        {
            (positions, indices) = ([], []);
            Emit(rootPath, objectId, transform, 0);
            if (indices.Count == 0) return;
            triangles += indices.Count / 3;
            // the model is shown in millimetres whatever unit the file was drawn in
            var scale = (float)root.MillimetresPerUnit;
            var name = root.Objects.TryGetValue(objectId, out var model) && !string.IsNullOrWhiteSpace(model.Name) ? model.Name.Trim() : "Objet " + (objects.Count + 1);
            objects.Add((name, new MeshModel(positions.Select(p => p * scale).ToArray(), [.. indices])));
        }

        if (root.Build.Count > 0)
            foreach (var (objectId, transform) in root.Build) Item(objectId, transform);
        else
            // a file without a build section: show every object that has a shape of its own
            foreach (var (objectId, _) in root.Objects.Where(pair => pair.Value.HasMesh)) Item(objectId, Matrix4x4.Identity);

        if (objects.Count == 0) throw new InvalidDataException("Le fichier 3MF ne contient aucune géométrie affichable.");
        if (triangles > MaxTriangles) throw new InvalidDataException("Ce fichier 3MF contient trop de triangles pour être affiché.");
        return objects;
    }

    // ---- archive ------------------------------------------------------------------------------------------------------

    private static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');

    private static ZipArchiveEntry? Find(ZipArchive archive, string normalisedPath)
        => archive.Entries.FirstOrDefault(entry => string.Equals(Normalise(entry.FullName), normalisedPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The main model of the package, as its relationships name it (usually <c>3D/3dmodel.model</c>).</summary>
    private static string RootModelPath(ZipArchive archive)
    {
        if (Find(archive, RelationshipsPath) is { } relationships)
        {
            using var stream = relationships.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
                var type = reader.GetAttribute("Type");
                if (type is not null && type.EndsWith("/3dmodel", StringComparison.OrdinalIgnoreCase) && reader.GetAttribute("Target") is { Length: > 0 } target)
                    return Normalise(target);
            }
        }
        return DefaultModelPath;
    }

    // ---- one model file -------------------------------------------------------------------------------------------------

    private static ModelPart ReadPart(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var part = new ModelPart();
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        ModelObject? current = null;
        var currentId = 0;
        var triangles = 0;
        while (reader.Read())
        {
            if ((triangles & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "object" && current is not null) { part.Objects[currentId] = current; current = null; }
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.LocalName)
            {
                case "model":
                    part.MillimetresPerUnit = Unit(reader.GetAttribute("unit"));
                    break;
                case "object":
                    currentId = Int(reader.GetAttribute("id"));
                    current = new ModelObject { Type = reader.GetAttribute("type"), Name = reader.GetAttribute("name") };
                    if (reader.IsEmptyElement) { part.Objects[currentId] = current; current = null; }
                    break;
                case "vertex" when current is not null:
                    current.Vertices.Add(new Vector3(Float(reader.GetAttribute("x")), Float(reader.GetAttribute("y")), Float(reader.GetAttribute("z"))));
                    break;
                case "triangle" when current is not null:
                    var a = Int(reader.GetAttribute("v1")); var b = Int(reader.GetAttribute("v2")); var c = Int(reader.GetAttribute("v3"));
                    if (a < 0 || b < 0 || c < 0 || a >= current.Vertices.Count || b >= current.Vertices.Count || c >= current.Vertices.Count)
                        throw new InvalidDataException("Fichier 3MF invalide : un triangle référence un sommet inexistant.");
                    current.Triangles.Add(a); current.Triangles.Add(b); current.Triangles.Add(c);
                    if (++triangles > MaxTriangles) throw new InvalidDataException("Ce fichier 3MF contient trop de triangles pour être affiché.");
                    break;
                case "component" when current is not null:
                    current.Components.Add(new Component(reader.GetAttribute("path", ProductionNamespace) ?? reader.GetAttribute("p:path"),
                        Int(reader.GetAttribute("objectid")), Transform(reader.GetAttribute("transform"))));
                    break;
                case "item":
                    // an object marked as not printable (a reference model, a modifier) is not part of the plate
                    if (reader.GetAttribute("printable") is "0" or "false") break;
                    part.Build.Add((Int(reader.GetAttribute("objectid")), Transform(reader.GetAttribute("transform"))));
                    break;
            }
        }
        return part;
    }

    // ---- values -------------------------------------------------------------------------------------------------------------

    private static double Unit(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "micron" => 0.001,
        "centimeter" => 10,
        "inch" => 25.4,
        "foot" => 304.8,
        "meter" => 1000,
        _ => 1 // millimeter, the default of the format
    };

    private static int Int(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static float Float(string? text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value : throw new InvalidDataException("Fichier 3MF invalide : coordonnée illisible.");

    /// <summary>The 3MF "m00 m01 m02 m10 … m32" matrix: the same row-vector layout as <see cref="Matrix4x4"/>.</summary>
    private static Matrix4x4 Transform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Matrix4x4.Identity;
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 12) throw new InvalidDataException("Fichier 3MF invalide : matrice de position incorrecte.");
        var m = new float[12];
        for (var i = 0; i < 12; i++) m[i] = Float(parts[i]);
        return new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
    }
}

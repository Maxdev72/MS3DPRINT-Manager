using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

internal sealed class ProjectReferenceReservations(string workspaceRoot)
{
    private readonly string _root = System.IO.Path.GetFullPath(workspaceRoot);
    private string Metadata => Path.Combine(_root, ".ms3dprint-manager");
    public string DirectoryPath => Path.Combine(Metadata, "project-references");

    public IReadOnlyList<string> ExistingNames()
    {
        var names = new List<string>();
        foreach (var path in WalkDirectories(Path.Combine(_root, "01_CLIENTS"))) names.Add(Path.GetFileName(path));
        foreach (var file in Files(Path.Combine(Metadata, "projects"), "*.json")) ReadReference(file, names, required: true);
        foreach (var file in Files(Path.Combine(Metadata, "history"), "*.json")) ReadReference(file, names, required: false);
        foreach (var file in Files(DirectoryPath, "*.json")) ReadReference(file, names, required: true);
        foreach (var entry in Directories(Path.Combine(Metadata, "trash")))
        {
            var manifestPath = Path.Combine(entry, "manifest.json");
            WorkspacePathSafety.EnsureNoLinks(manifestPath);
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var restored = manifest.RootElement.TryGetProperty("State", out var state) && state.GetString() == "Restored";
            foreach (var item in manifest.RootElement.GetProperty("Items").EnumerateArray())
            {
                var originalName = Path.GetFileName(item.GetProperty("Original").GetString()!);
                names.Add(originalName);
                if (restored) continue;
                var payloadName = item.GetProperty("Payload").GetString();
                if (string.IsNullOrWhiteSpace(payloadName) || payloadName is "." or ".." || payloadName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new JsonException("Le chemin de corbeille est invalide.");
                var payload = Path.Combine(entry, payloadName);
                WorkspacePathSafety.EnsureNoLinks(payload);
                if (item.GetProperty("IsDirectory").GetBoolean())
                {
                    if (!ProjectReferenceFormat.TryParseFolderName(originalName.ToUpperInvariant(), out _))
                        foreach (var path in WalkDirectories(payload)) names.Add(Path.GetFileName(path));
                }
                else if (item.GetProperty("Original").GetString()!.Replace('\\', '/').StartsWith(".ms3dprint-manager/projects/", StringComparison.OrdinalIgnoreCase))
                    ReadReference(payload, names, required: true);
            }
        }
        return names;
    }

    public void Reserve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || reference is "." or "..")
            throw new ArgumentException("La référence projet est invalide.", nameof(reference));
        var destination = Path.Combine(DirectoryPath, reference.ToUpperInvariant() + ".json");
        WorkspacePathSafety.EnsureNoLinks(destination);
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(DirectoryPath);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new { Reference = reference });
                stream.Flush(true);
            }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ReadReference(string file, List<string> names, bool required)
    {
        WorkspacePathSafety.EnsureNoLinks(file);
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Les métadonnées projet sont invalides.");
        if (json.RootElement.TryGetProperty("Reference", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            names.Add(value.GetString() + "_");
        else if (required) throw new JsonException("La référence projet est absente des métadonnées.");
    }

    private static IEnumerable<string> Files(string directory, string pattern)
    {
        WorkspacePathSafety.EnsureNoLinks(directory);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern) : [];
    }

    private static IEnumerable<string> Directories(string directory)
    {
        WorkspacePathSafety.EnsureNoLinks(directory);
        return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [];
    }

    private static IEnumerable<string> WalkDirectories(string directory)
    {
        foreach (var child in Directories(directory))
        {
            if (Path.GetFileName(child).StartsWith(".ms3dprint", StringComparison.OrdinalIgnoreCase)) continue;
            WorkspacePathSafety.EnsureNoLinks(child);
            yield return child;
            // A project is a boundary: its classified document folders cannot reserve references.
            if (ProjectReferenceFormat.TryParseFolderName(Path.GetFileName(child).ToUpperInvariant(), out _)) continue;
            foreach (var descendant in WalkDirectories(child)) yield return descendant;
        }
    }
}

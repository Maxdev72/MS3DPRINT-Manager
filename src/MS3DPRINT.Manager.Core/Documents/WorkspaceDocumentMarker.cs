using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Documents;

/// <summary>
/// <c>.ms3dforge-workspace.json</c> at the root of the Nextcloud folder. It helps find the folder on each PC;
/// it grants no right: the authenticated API validates the binding. Holds no secret.
/// </summary>
public sealed record WorkspaceDocumentMarker(int SchemaVersion, Guid WorkspaceId, Guid StorageBindingId, Guid MarkerId)
{
    public const string FileName = ".ms3dforge-workspace.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static WorkspaceDocumentMarker? Read(string root)
    {
        try
        {
            var path = Path.Combine(root, FileName);
            if (!File.Exists(path)) return null;
            var marker = JsonSerializer.Deserialize<WorkspaceDocumentMarker>(File.ReadAllBytes(path), Json);
            return marker is { SchemaVersion: 1 } && marker.WorkspaceId != Guid.Empty && marker.StorageBindingId != Guid.Empty && marker.MarkerId != Guid.Empty
                ? marker : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Writes the marker once; an existing marker of another workspace is never replaced.</summary>
    public void WriteTo(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Le dossier documentaire est introuvable.");
        var path = Path.Combine(root, FileName);
        if (File.Exists(path))
        {
            if (Read(root) is { } existing && existing.Matches(WorkspaceId, StorageBindingId)) return;
            throw new InvalidOperationException("Ce dossier porte déjà le marqueur d’un autre atelier ; il n’a pas été modifié.");
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, this, Json);
        stream.Flush(flushToDisk: true);
    }

    public bool Matches(Guid workspaceId, Guid storageBindingId) => WorkspaceId == workspaceId && StorageBindingId == storageBindingId;
}

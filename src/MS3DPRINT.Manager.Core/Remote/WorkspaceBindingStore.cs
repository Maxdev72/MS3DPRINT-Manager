using System.Text.Json;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Remote;

/// <summary>
/// Per-account binding to the local documents folder, plus the offline-read authorisation granted by a successful sign-in.
/// A binding to another workspace or storage is never overwritten; an explicit sign-out removes the authorisation only.
/// </summary>
public sealed class WorkspaceBindingStore(AccountWorkspacePaths paths)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string OfflinePath => Path.Combine(paths.DirectoryPath, "offline-authorized");

    public LocalWorkspaceBinding? Load()
    {
        if (!File.Exists(paths.BindingPath)) return null;
        try
        {
            var binding = JsonSerializer.Deserialize<LocalWorkspaceBinding>(File.ReadAllBytes(paths.BindingPath), Json);
            return binding is not null && binding.WorkspaceId != Guid.Empty && binding.StorageBindingId != Guid.Empty
                && binding.LocalDocumentsRoot is { } root && Path.IsPathFullyQualified(root) ? binding : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    public void Save(LocalWorkspaceBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.WorkspaceId == Guid.Empty || binding.StorageBindingId == Guid.Empty || !Path.IsPathFullyQualified(binding.LocalDocumentsRoot))
            throw new ArgumentException("Association documentaire invalide.", nameof(binding));
        if (Load() is { } existing && (existing.WorkspaceId != binding.WorkspaceId || existing.StorageBindingId != binding.StorageBindingId))
            throw new InvalidOperationException("Ce PC est déjà associé à un autre atelier ou stockage. L’association existante est conservée.");
        Directory.CreateDirectory(paths.DirectoryPath);
        var temporary = paths.BindingPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(binding with
            {
                LocalDocumentsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binding.LocalDocumentsRoot))
            }, Json));
            File.Move(temporary, paths.BindingPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool IsOfflineAuthorized => File.Exists(OfflinePath);

    public void GrantOfflineAfterSignIn()
    {
        Directory.CreateDirectory(paths.DirectoryPath);
        File.WriteAllText(OfflinePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    public void RevokeOfflineOnSignOut() { if (File.Exists(OfflinePath)) File.Delete(OfflinePath); }
}

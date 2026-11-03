using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Remote;

public sealed record RemoteWorkspaceCopy(string Server, string Account, DateTimeOffset? ReadAt,
    IReadOnlyList<ClientResponse> Clients, IReadOnlyList<ProjectResponse> Projects, Guid? PendingCommand);

/// <summary>Consultation data in the OS user's private application folder, never an active local workshop or a credential store.</summary>
public sealed class RemoteWorkspaceCache(string path)
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public IDisposable AcquireLease()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        WorkspacePathSafety.EnsureNoLinks(_path + ".lock");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // The handle is released by the OS even after a crash. A second Manager must not overwrite pending receipts.
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public RemoteWorkspaceCopy? Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > MaximumBytes) throw new InvalidDataException("Copie distante trop volumineuse.");
        try
        {
            var copy = JsonSerializer.Deserialize<RemoteWorkspaceCopy>(File.ReadAllBytes(_path), Json)
                ?? throw new InvalidDataException("Copie distante vide.");
            Validate(copy);
            return copy;
        }
        catch (JsonException ex) { throw new InvalidDataException("Copie distante illisible ; reconnectez-vous pour la reconstruire.", ex); }
    }

    public void Save(RemoteWorkspaceCopy copy)
    {
        Validate(copy);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(copy, Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Copie distante trop volumineuse.");
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            WorkspacePathSafety.EnsureNoLinks(_path);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Clear() { WorkspacePathSafety.EnsureNoLinks(_path); File.Delete(_path); }

    public static void Validate(RemoteWorkspaceCopy copy)
    {
        if (!Uri.TryCreate(copy.Server, UriKind.Absolute, out var server) || server.Scheme != "https"
            || server.AbsolutePath != "/" || server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(copy.Account) || copy.Account.Length > 100 || copy.Account.Any(char.IsControl)
            || copy.Clients is null || copy.Projects is null || copy.PendingCommand == Guid.Empty)
            throw new InvalidDataException("Copie distante invalide (serveur, compte ou contenu).");
        var clients = new HashSet<Guid>();
        foreach (var client in copy.Clients)
            if (client is null || client.Id == Guid.Empty || !clients.Add(client.Id)
                || !Enum.TryParse<ClientKind>(client.Kind, out var kind) || !Enum.IsDefined(kind)
                || string.IsNullOrWhiteSpace(client.ClientCode) || string.IsNullOrWhiteSpace(client.DisplayName))
                throw new InvalidDataException("Fiches clients de la copie distante invalides.");
        var projects = new HashSet<Guid>();
        foreach (var project in copy.Projects)
        {
            if (project is null || project.Id == Guid.Empty || !projects.Add(project.Id) || !clients.Contains(project.ClientId)
                || !Enum.TryParse<ProjectStatus>(project.Status, out var status) || !Enum.IsDefined(status)
                || !Enum.TryParse<ProjectPriority>(project.Priority, out var priority) || !Enum.IsDefined(priority)
                || string.IsNullOrWhiteSpace(project.Reference) || string.IsNullOrWhiteSpace(project.ProjectName) || project.Tasks is null)
                throw new InvalidDataException("Fiches projets de la copie distante invalides.");
            var tasks = new HashSet<Guid>();
            foreach (var task in project.Tasks)
                if (task is null || task.Id == Guid.Empty || !tasks.Add(task.Id) || string.IsNullOrWhiteSpace(task.Title))
                    throw new InvalidDataException("Tâches de la copie distante invalides.");
        }
    }
}

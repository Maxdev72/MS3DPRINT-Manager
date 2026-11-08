using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Workspace;

public sealed class WorkspaceCommands
{
    private readonly string _root;
    private readonly WorkspaceMetadataPaths _paths;
    private readonly ClientProfileStore _clients;
    private readonly ProjectProfileStore _projects;

    public WorkspaceCommands(ServerStorage storage)
    {
        _root = storage.WorkspaceRoot;
        _paths = new(_root);
        _clients = new(_paths);
        _projects = new(_paths);
    }

    public ClientDetailResponse Client(Guid id) => Detail(LoadClient(id));
    public ProjectDetailResponse Project(Guid id) => Detail(LoadProject(id));

    public CommandResult Create(CreateClientCommand command)
    {
        ValidateClient(command.Kind, command.CompanyName, command.FirstName, command.LastName, command.Address, command.Notes, command.Contact);
        var code = NormalizeRequired(command.ClientCode, 32);
        if (_clients.LoadAll().Any(existing => string.Equals(NameNormalizer.Normalize(existing.ClientCode), code, StringComparison.Ordinal)))
            return new(409, new { error = "Ce code est déjà attribué à un autre client. Choisissez un code différent." });
        var folder = NormalizeRequired(command.Kind == ClientKind.Professional ? command.CompanyName : string.Join(" ", command.FirstName, command.LastName), 200);
        Portable(folder);
        if (folder is "00_CLIENT" or "99_ARCHIVES") throw new ArgumentException();
        var relative = "01_CLIENTS/" + folder;
        var destination = SafePath(relative);
        EnsureNoCaseCollision(destination);
        // The Core registry accesses several siblings; reject links before giving it this directory.
        var registry = Path.Combine(_paths.MetadataDirectory, "client-codes");
        EnsureTreeWithoutLinks(registry);
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfile(Guid.NewGuid(), command.Kind, folder, code, Clean(command.CompanyName),
            Clean(command.FirstName), Clean(command.LastName), Clean(command.Address), Clean(command.Notes), command.Contact,
            now, now, RelativePath: relative);
        return new(201, Detail(new ClientCreationService(_root, new FolderTreeService(), _clients, new ClientCodeRegistry(registry)).Create(profile)));
    }

    public CommandResult Update(Guid id, UpdateClientCommand command)
    {
        var profile = LoadClient(id);
        ValidateClient(command.Kind, command.CompanyName, command.FirstName, command.LastName, command.Address, command.Notes, command.Contact);
        Version(profile.UpdatedAt, command.ExpectedUpdatedAt);
        return new(200, Detail(_clients.Update(profile with
        {
            Kind = command.Kind, CompanyName = Clean(command.CompanyName), FirstName = Clean(command.FirstName),
            LastName = Clean(command.LastName), Address = Clean(command.Address), Notes = Clean(command.Notes), PrimaryContact = command.Contact
        }, command.ExpectedUpdatedAt)));
    }

    public CommandResult Create(CreateProjectCommand command)
    {
        var client = LoadClient(command.ClientId);
        NormalizeRequired(command.ProjectName, 200);
        NormalizeRequired(client.ClientCode, 32);
        if (command.Year is < 1000 or > 9999 || !Enum.IsDefined(command.Priority)) throw new ArgumentException();
        Text(command.Description, 10000);
        var clientPath = ClientPath(client);
        var summary = new ClientSummary(clientPath, client.FolderName, DisplayName(client), client.ClientCode, client.Kind, client, 0);
        EnsureTreeWithoutLinks(Path.Combine(_paths.MetadataDirectory, "project-references"));
        // Project templates are user metadata; validate all segments with Windows rules even on Linux.
        WorkspacePathSafety.EnsureNoLinks(Path.Combine(_paths.MetadataDirectory, "project-template.json"));
        var template = new MS3DPRINT.Manager.Core.Templates.ProjectTemplateStore(_paths).Load();
        var prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in template.Paths)
        {
            var prefix = "";
            foreach (var segment in path.Split(['/', '\\']))
            {
                Portable(segment);
                prefix += "/" + segment;
                if (prefixes.TryGetValue(prefix, out var existing) && existing != prefix) throw new ArgumentException();
                prefixes[prefix] = prefix;
            }
        }
        var created = new ProjectCreationService(new FolderTreeService()).CreateWithProfile(summary, command.Year,
            command.ProjectName, _projects, command.DueDate, Clean(command.Description), command.Priority);
        return new(201, Detail(created.Profile));
    }

    public CommandResult Update(Guid id, UpdateProjectCommand command)
    {
        var profile = LoadProject(id);
        Version(profile.UpdatedAt, command.ExpectedUpdatedAt);
        if (!Enum.IsDefined(command.Status) || !Enum.IsDefined(command.Priority)) throw new ArgumentException();
        Text(command.Description, 10000); Text(command.Notes, 10000);
        ProjectTasks.Validate(command.Tasks);
        return new(200, Detail(_projects.Update(profile with
        {
            Status = command.Status, DueDate = command.DueDate, Description = Clean(command.Description), Notes = Clean(command.Notes), Priority = command.Priority,
            // Null keeps the tasks (website form); a list replaces them (sheet synchronized from Manager).
            Tasks = command.Tasks is null ? profile.Tasks : command.Tasks.Select(t => t with { Title = t.Title.Trim(), Notes = Clean(t.Notes) }).ToArray()
        }, command.ExpectedUpdatedAt)));
    }

    public CommandResult AddTask(Guid id, AddTaskCommand command)
        => ChangeTasks(id, null, command.ExpectedUpdatedAt, profile => ProjectTasks.Add(_projects, profile, RequiredTitle(command.Title), command.Notes));
    public CommandResult EditTask(Guid id, Guid taskId, EditTaskCommand command)
        => ChangeTasks(id, taskId, command.ExpectedUpdatedAt, profile => ProjectTasks.Edit(_projects, profile, taskId, RequiredTitle(command.Title), command.Notes));
    public CommandResult CompleteTask(Guid id, Guid taskId, CompleteTaskCommand command)
        => ChangeTasks(id, taskId, command.ExpectedUpdatedAt, profile => ProjectTasks.SetCompleted(_projects, profile, taskId, command.Completed));
    public CommandResult MoveTask(Guid id, Guid taskId, MoveTaskCommand command)
        => ChangeTasks(id, taskId, command.ExpectedUpdatedAt, profile => ProjectTasks.Move(_projects, profile, taskId, command.Direction));
    public CommandResult RemoveTask(Guid id, Guid taskId, RemoveTaskCommand command)
    {
        if (!command.Confirmed) throw new ArgumentException();
        return ChangeTasks(id, taskId, command.ExpectedUpdatedAt, profile => ProjectTasks.Remove(_projects, profile, taskId));
    }

    private CommandResult ChangeTasks(Guid id, Guid? taskId, DateTimeOffset version, Func<ProjectProfile, ProjectProfile> change)
    {
        var profile = LoadProject(id);
        Version(profile.UpdatedAt, version);
        if (taskId == Guid.Empty) throw new ArgumentException();
        if (taskId is { } task && !ProjectTasks.Items(profile).Any(t => t.Id == task)) throw new KeyNotFoundException();
        return new(200, Detail(change(profile)));
    }

    private ClientProfile LoadClient(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException();
        var profile = _clients.LoadAll().SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException();
        ClientPath(profile);
        return profile;
    }

    private ProjectProfile LoadProject(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException();
        var profile = _projects.LoadAll().SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException();
        StoredFolder(profile.FolderName);
        var client = LoadClient(profile.ClientId);
        var clientPath = ClientPath(client);
        var projectPath = SafePath(profile.RelativePath ?? Path.GetRelativePath(_root, Path.Combine(clientPath, profile.FolderName)).Replace('\\', '/'));
        if (!WorkspaceEntityPaths.IsInside(clientPath, projectPath) || projectPath == clientPath) throw new InvalidDataException();
        if (!Directory.Exists(projectPath)) throw new KeyNotFoundException();
        return profile;
    }

    private string ClientPath(ClientProfile profile)
    {
        StoredFolder(profile.FolderName);
        var path = SafePath(profile.RelativePath ?? "01_CLIENTS/" + profile.FolderName);
        if (!Directory.Exists(path)) throw new KeyNotFoundException();
        return path;
    }

    private string SafePath(string relative)
    {
        // Linux Path APIs do not interpret a backslash or a Windows drive as a separator/root.
        if (relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new InvalidDataException();
        try
        {
            var path = WorkspaceEntityPaths.Resolve(_root, relative, "01_CLIENTS");
            WorkspacePathSafety.EnsureNoLinks(path);
            return path;
        }
        catch (ArgumentException exception) { throw new InvalidDataException("Chemin de fiche invalide.", exception); }
    }

    private static void StoredFolder(string folder)
    {
        try { Portable(folder); }
        catch (ArgumentException exception) { throw new InvalidDataException("Nom de dossier de fiche invalide.", exception); }
    }

    private static void EnsureNoCaseCollision(string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        if (Directory.Exists(parent) && Directory.EnumerateFileSystemEntries(parent).Any(entry =>
            string.Equals(Path.GetFileName(entry), Path.GetFileName(destination), StringComparison.OrdinalIgnoreCase)))
            throw new FolderConflictException("Ce nom de dossier est déjà utilisé.");
    }

    private static void EnsureTreeWithoutLinks(string root)
    {
        WorkspacePathSafety.EnsureNoLinks(root);
        if (!Directory.Exists(root)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            WorkspacePathSafety.EnsureNoLinks(entry);
            if (Directory.Exists(entry)) EnsureTreeWithoutLinks(entry);
        }
    }

    private static void ValidateClient(ClientKind kind, string? company, string? first, string? last, string? address, string? notes, PrimaryContact? contact)
    {
        if (!Enum.IsDefined(kind) || contact is null) throw new ArgumentException();
        Text(company, 200); Text(first, 200); Text(last, 200); Text(address, 2000); Text(notes, 10000);
        if (kind == ClientKind.Professional) NormalizeRequired(company, 200); else NormalizeRequired(last, 200);
        Text(contact.FirstName, 200); Text(contact.LastName, 200); Text(contact.Role, 200);
        Text(contact.Phone, 50); Text(contact.Mobile, 50); Text(contact.Email, 254);
    }

    private static string NormalizeRequired(string? value, int max)
    {
        Text(value, max);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException();
        var normalized = NameNormalizer.Normalize(value);
        if (normalized.Length == 0 || normalized.Length > max) throw new ArgumentException();
        return normalized;
    }
    private static string RequiredTitle(string title)
    {
        Text(title, 500);
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException();
        return title;
    }
    private static void Text(string? value, int max)
    {
        if (value?.Length > max || value?.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) == true) throw new ArgumentException();
    }
    private static void Portable(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (name.Length is 0 or > 255 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
            || stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$"
            || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9') throw new ArgumentException();
    }
    private static void Version(DateTimeOffset actual, DateTimeOffset expected)
    {
        if (expected == default) throw new ArgumentException();
        ProfileMutationLock.CheckVersion(actual, expected);
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string DisplayName(ClientProfile p) => p.Kind == ClientKind.Professional ? p.CompanyName! : string.Join(" ", p.FirstName, p.LastName).Trim();
    private static ClientDetailResponse Detail(ClientProfile p) => new(p.Id, p.Kind, p.FolderName, p.ClientCode, p.CompanyName,
        p.FirstName, p.LastName, p.Address, p.Notes, p.PrimaryContact, p.CreatedAt, p.UpdatedAt, p.Siret);
    private static ProjectDetailResponse Detail(ProjectProfile p) => new(p.Id, p.ClientId, p.ClientCode, p.Reference, p.FolderName,
        p.ProjectName, p.Status, p.CreatedAt, p.DueDate, p.Description, p.Notes, p.UpdatedAt, p.Priority, ProjectTasks.Items(p));
}

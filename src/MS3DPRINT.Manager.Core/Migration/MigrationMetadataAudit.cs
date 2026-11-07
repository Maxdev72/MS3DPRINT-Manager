using System.Text.Json;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Migration;

internal static class MigrationMetadataAudit
{
    internal static IReadOnlyList<MigrationIssue> Audit(string root, MigrationInventory inventory)
    {
        var issues = new List<MigrationIssue>();
        foreach (var file in inventory.Files.Where(f => (f.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            && (f.Path.StartsWith(".ms3dprint-manager/", StringComparison.Ordinal) || f.Path.StartsWith("data/", StringComparison.Ordinal))))
        {
            try
            {
                using var input = File.OpenRead(Path.Combine(root, file.Path));
                // shortcut: metadata audit is capped at 16 MiB per file; add streaming schema validation before accepting larger journals.
                if (input.Length > 16 * 1024 * 1024)
                {
                    issues.Add(new("MetadataTooLarge", file.Path, "Ce fichier de métadonnées exige un contrôle en flux avant migration (limite actuelle : 16 Mio)."));
                    continue;
                }
                if (file.Path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    using var lines = new StreamReader(input);
                    while (lines.ReadLine() is { } line)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        using var entry = JsonDocument.Parse(line);
                        Walk(entry.RootElement, file.Path, issues);
                    }
                    continue;
                }
                using var json = JsonDocument.Parse(input);
                Walk(json.RootElement, file.Path, issues);
                if (file.Path.StartsWith(".ms3dprint-manager/clients/", StringComparison.Ordinal)
                    || file.Path.StartsWith(".ms3dprint-manager/projects/", StringComparison.Ordinal))
                {
                    if (!json.RootElement.TryGetProperty("Id", out var id) || !id.TryGetGuid(out var value)
                        || !string.Equals(Path.GetFileNameWithoutExtension(file.Path), value.ToString(), StringComparison.OrdinalIgnoreCase))
                        issues.Add(new("ProfileIdentity", file.Path, "L’identifiant de la fiche et son nom de fichier ne correspondent pas."));
                }
            }
            catch (JsonException) { issues.Add(new("InvalidJson", file.Path, "Le JSON de métadonnées est illisible.")); }
        }
        if (issues.Any(i => i.Code is "InvalidJson" or "ProfileIdentity")) return issues;
        var paths = new WorkspaceMetadataPaths(root);
        try
        {
            var clients = new ClientProfileStore(paths).LoadAll();
            var projects = new ProjectProfileStore(paths).LoadAll();
            var byId = new Dictionary<Guid, ClientProfile>();
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clientFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var client in clients)
            {
                var path = ".ms3dprint-manager/clients/" + client.Id + ".json";
                if (client.Id == Guid.Empty || !byId.TryAdd(client.Id, client) || !Enum.IsDefined(client.Kind)
                    || string.IsNullOrWhiteSpace(client.ClientCode) || client.PrimaryContact is null)
                    issues.Add(new("InvalidClient", path, "La fiche client est incomplète ou possède un identifiant invalide."));
                if (!codes.Add(client.ClientCode) || !clientFolders.Add(client.RelativePath ?? "01_CLIENTS/" + client.FolderName))
                    issues.Add(new("DuplicateClient", path, "Un code client ou un dossier est attribué à plusieurs fiches."));
                CheckFolder(root, client.RelativePath ?? "01_CLIENTS/" + client.FolderName, path, issues);
            }
            var projectIds = new HashSet<Guid>();
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projectFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects)
            {
                var path = ".ms3dprint-manager/projects/" + project.Id + ".json";
                if (project.Id == Guid.Empty || !projectIds.Add(project.Id) || !Enum.IsDefined(project.Status)
                    || !Enum.IsDefined(project.Priority) || string.IsNullOrWhiteSpace(project.Reference) || string.IsNullOrWhiteSpace(project.ProjectName))
                    issues.Add(new("InvalidProject", path, "La fiche projet est incomplète ou possède un identifiant invalide."));
                if (!byId.TryGetValue(project.ClientId, out var client))
                {
                    issues.Add(new("MissingClient", path, "Le client référencé par le projet est absent."));
                    continue;
                }
                if (project.ClientCode != client.ClientCode)
                    issues.Add(new("ClientCodeMismatch", path, "Le code client du projet ne correspond pas à sa fiche client."));
                var clientPath = client.RelativePath ?? "01_CLIENTS/" + client.FolderName;
                var relative = project.RelativePath ?? clientPath + "/" + project.FolderName;
                if (!references.Add(project.Reference) || !projectFolders.Add(relative))
                    issues.Add(new("DuplicateProject", path, "Une référence projet ou un dossier est attribué à plusieurs fiches."));
                CheckFolder(root, relative, path, issues);
                var parent = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
                if (parent != clientPath && parent != clientPath + "/99_ARCHIVES")
                    issues.Add(new("ProjectLocation", path, "Le dossier du projet ne se trouve pas sous son client."));
            }
            var indexed = clients.Select(c => c.RelativePath ?? "01_CLIENTS/" + c.FolderName).ToArray();
            var missing = new List<string>();
            foreach (var directory in inventory.Directories.Where(d => d.StartsWith("01_CLIENTS/", StringComparison.Ordinal)))
            {
                bool Below(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
                if (indexed.Any(client => Below(directory, client) || Below(client, directory)) || missing.Any(parent => Below(directory, parent))) continue;
                missing.Add(directory);
                issues.Add(new("ClientProfileMissing", directory, "Ce dossier client n’a pas de fiche ; il exige une reprise explicite avant migration."));
            }
            foreach (var project in new ProjectCatalog(new ProjectProfileStore(paths)).Load(root).Where(p => p.Profile is null))
                issues.Add(new("ProjectProfileMissing", Path.GetRelativePath(root, project.ProjectPath).Replace('\\', '/'), "Dossier projet sans fiche : conservé avec tous ses fichiers, sans créer de fiche ni de tâches.", false));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            issues.Add(new("InvalidProfiles", ".ms3dprint-manager", "Les fiches métier ne peuvent pas être relues et validées."));
        }
        return issues;
    }

    private static void CheckFolder(string root, string relative, string profile, List<MigrationIssue> issues)
    {
        try
        {
            Storage.WorkspaceBackupService.ValidateRelative(relative);
            var full = WorkspaceEntityPaths.Resolve(root, relative, "01_CLIENTS");
            WorkspacePathSafety.EnsureNoLinks(full);
            if (!Directory.Exists(full)) issues.Add(new("MissingFolder", profile, "Le dossier de la fiche est absent."));
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException)
        {
            issues.Add(new("InvalidProfilePath", profile, "Le chemin de la fiche doit être relatif, portable et dans sa catégorie."));
        }
    }

    internal static void Walk(JsonElement value, string path, List<MigrationIssue> issues)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (property.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                     || property.Name.EndsWith("Directory", StringComparison.OrdinalIgnoreCase)
                     || property.Name.EndsWith("Root", StringComparison.OrdinalIgnoreCase)))
                {
                    var text = property.Value.GetString()!;
                    if (text.StartsWith('/') || text.StartsWith('\\') || (text.Length >= 2 && char.IsLetter(text[0]) && text[1] == ':'))
                        issues.Add(new("AbsoluteReference", path, "Le champ " + property.Name + " contient un chemin absolu à convertir explicitement."));
                    else if (text.Contains('\\'))
                        issues.Add(new("WindowsSeparator", path, "Le champ " + property.Name + " utilise des séparateurs Windows à convertir explicitement."));
                    else if (text.Length > 0 && text.Split('/').Any(part => part is "" or "." or ".."))
                        issues.Add(new("RelativeTraversal", path, "Le champ " + property.Name + " contient un chemin relatif ambigu ou une remontée de dossier."));
                }
                Walk(property.Value, path, issues);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) Walk(item, path, issues);
    }
}

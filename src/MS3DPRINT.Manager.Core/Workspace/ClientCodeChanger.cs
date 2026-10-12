using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Workspace;

/// <summary>One project folder that gets another reference when the client code changes.</summary>
public sealed record ClientCodeRename(string OldReference, string NewReference, string OldFolderPath, string NewFolderName, ProjectProfile? Profile, int EstimateCount)
{
    public string OldFolderName => Path.GetFileName(OldFolderPath);
    public string NewFolderPath => Path.Combine(Path.GetDirectoryName(OldFolderPath)!, NewFolderName);
}

/// <summary>What changing a client code would rename, shown to the user before anything is touched.</summary>
public sealed record ClientCodeChangePlan(ClientSummary Client, string OldCode, string NewCode, IReadOnlyList<ClientCodeRename> Projects, IReadOnlyList<string> Skipped);

/// <summary>
/// Changes the code of a client and carries it into everything that spells it: the client fiche, the folder and fiche of each
/// project (F-2026-001 becomes FA-2026-001), the reserved references and the estimates linked to those projects.
/// Everything is checked first (<see cref="Plan"/>); <see cref="Apply"/> undoes what it did if a step fails.
/// </summary>
public sealed partial class ClientCodeChanger(string workspaceRoot)
{
    private readonly string _root = Path.GetFullPath(workspaceRoot);

    [GeneratedRegex("^[A-Z0-9]{1,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    /// <summary>The code as it will be written: upper case, letters and digits only; null when it is not acceptable.</summary>
    public static string? NormalizeCode(string? code)
    {
        var upper = (code ?? string.Empty).Trim().ToUpperInvariant();
        return CodePattern().IsMatch(upper) ? upper : null;
    }

    public ClientCodeChangePlan Plan(ClientSummary client, string newCode, IEnumerable<ClientSummary> allClients)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(allClients);
        if (client.Profile is null) throw new ArgumentException("Complétez la fiche client avant de changer son code.");
        var code = NormalizeCode(newCode) ?? throw new ArgumentException("Le code client doit compter de 1 à 6 lettres ou chiffres, sans espace ni accent.");
        var oldCode = client.ClientCode;
        if (string.Equals(code, oldCode, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Le nouveau code est identique à l’ancien.");
        if (allClients.Any(other => !string.Equals(other.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(other.ClientCode, code, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Le code {code} est déjà utilisé par un autre client.");

        var paths = new WorkspaceMetadataPaths(_root);
        var projects = new ProjectCatalog(new ProjectProfileStore(paths)).Load(_root)
            .Where(project => string.Equals(project.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)).ToList();
        var taken = new ProjectReferenceReservations(_root).ExistingNames();
        var estimates = new EstimateStore(_root).LoadAll();
        var renames = new List<ClientCodeRename>();
        var skipped = new List<string>();
        var prefix = oldCode.ToUpperInvariant() + "-";
        foreach (var project in projects)
        {
            if (!project.Reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !project.FolderName.StartsWith(project.Reference, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(project.FolderName);
                continue;
            }
            var newReference = code + project.Reference[oldCode.Length..];
            var newFolder = newReference + project.FolderName[project.Reference.Length..];
            if (taken.Any(name => name.StartsWith(newReference + "_", StringComparison.OrdinalIgnoreCase) || string.Equals(name, newReference, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"La référence {newReference} existe déjà : choisissez un autre code.");
            if (Directory.Exists(Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, newFolder)))
                throw new ArgumentException($"Le dossier {newFolder} existe déjà.");
            var count = estimates.Count(estimate => string.Equals(estimate.ProjectReference, project.Reference, StringComparison.OrdinalIgnoreCase));
            renames.Add(new ClientCodeRename(project.Reference, newReference, project.ProjectPath, newFolder, project.Profile, count));
        }
        return new ClientCodeChangePlan(client, oldCode, code, renames, skipped);
    }

    public void Apply(ClientCodeChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var profile = plan.Client.Profile ?? throw new ArgumentException("Complétez la fiche client avant de changer son code.");
        var paths = new WorkspaceMetadataPaths(_root);
        var files = new ManagedFileService(_root);
        var projectStore = new ProjectProfileStore(paths);
        var reservations = new ProjectReferenceReservations(_root);
        var estimateStore = new EstimateStore(_root);
        var estimates = estimateStore.LoadAll();
        var undo = new Stack<Action>();
        try
        {
            foreach (var rename in plan.Projects)
            {
                files.Rename(rename.OldFolderPath, rename.NewFolderName);
                undo.Push(() => files.Rename(rename.NewFolderPath, rename.OldFolderName));

                if (rename.Profile is { } projectProfile)
                {
                    var relative = projectProfile.RelativePath is null ? null : ReplaceLastSegment(projectProfile.RelativePath, rename.NewFolderName);
                    var changed = projectStore.Update(projectProfile with
                    {
                        ClientCode = plan.NewCode, Reference = rename.NewReference, FolderName = rename.NewFolderName, RelativePath = relative
                    }, projectProfile.UpdatedAt);
                    undo.Push(() => projectStore.Update(projectProfile, changed.UpdatedAt));
                }

                reservations.Reserve(rename.NewReference);
                undo.Push(() => reservations.Release(rename.NewReference));
                // the old reference stays in the history of the project fiche, so it is never given out again
            }

            foreach (var estimate in estimates)
            {
                var rename = plan.Projects.FirstOrDefault(item => string.Equals(item.OldReference, estimate.ProjectReference, StringComparison.OrdinalIgnoreCase));
                if (rename is null) continue;
                estimateStore.Update(estimate with { ProjectReference = rename.NewReference });
                undo.Push(() => estimateStore.Update(estimate));
            }

            new ClientProfileStore(paths).Update(profile with { ClientCode = plan.NewCode }, profile.UpdatedAt);
        }
        catch
        {
            var failures = new List<string>();
            while (undo.Count > 0)
            {
                try { undo.Pop()(); }
                catch (Exception undoFailure) when (undoFailure is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                { failures.Add(undoFailure.Message); }
            }
            if (failures.Count > 0)
                throw new InvalidOperationException("Le changement de code a échoué et certains éléments n’ont pas pu être remis en l’état : " + string.Join(" ", failures));
            throw;
        }
    }

    private static string ReplaceLastSegment(string relativePath, string folderName)
    {
        var slash = relativePath.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? folderName : relativePath[..(slash + 1)] + folderName;
    }
}

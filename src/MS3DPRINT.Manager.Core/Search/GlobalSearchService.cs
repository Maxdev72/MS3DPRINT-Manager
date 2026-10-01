using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Search;

public enum GlobalSearchResultKind { Client, Project, File }

public sealed record GlobalSearchResult(
    GlobalSearchResultKind Kind,
    string Title,
    string Detail,
    string Path,
    ClientSummary? Client = null,
    ProjectSummary? Project = null);

public sealed record GlobalSearchResponse(IReadOnlyList<GlobalSearchResult> Results, bool HasMore);

public sealed class GlobalSearchService
{
    private readonly ClientCatalog _clients;
    private readonly ProjectCatalog _projects;

    public GlobalSearchService(ClientCatalog clients, ProjectCatalog projects)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    }

    public GlobalSearchResponse Search(string workspaceRoot, string query, int maxResults = 200, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults < 1) throw new ArgumentOutOfRangeException(nameof(maxResults));

        var root = Path.GetFullPath(workspaceRoot);
        var term = query.Trim();
        var results = new List<GlobalSearchResult>();

        bool Add(GlobalSearchResult result)
        {
            if (results.Count == maxResults) return false;
            results.Add(result);
            return true;
        }

        foreach (var client in _clients.Load(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(client.DisplayName, term) && !Matches(client.ClientCode, term) && !Matches(client.FolderName, term) &&
                !Matches(client.Profile?.PrimaryContact.FirstName, term) && !Matches(client.Profile?.PrimaryContact.LastName, term) &&
                !Matches(client.Profile?.PrimaryContact.Phone, term) && !Matches(client.Profile?.PrimaryContact.Email, term)) continue;
            if (!Add(new GlobalSearchResult(GlobalSearchResultKind.Client, client.DisplayName, $"Client · {client.ClientCode}", client.ClientPath, Client: client)))
                return new GlobalSearchResponse(results, true);
        }

        foreach (var project in _projects.Load(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(project.Reference, term) && !Matches(project.ProjectName, term) && !Matches(project.ClientFolderName, term)) continue;
            if (!Add(new GlobalSearchResult(GlobalSearchResultKind.Project, $"{project.Reference} · {project.ProjectName}", $"Projet · {project.ClientFolderName}", project.ProjectPath, Project: project)))
                return new GlobalSearchResponse(results, true);
        }

        if (!Directory.Exists(root)) return new GlobalSearchResponse(results, false);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] files;
            string[] subdirectories;
            try
            {
                files = Directory.GetFiles(directory);
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (!Matches(name, term)) continue;
                if (!Add(new GlobalSearchResult(GlobalSearchResultKind.File, name, Path.GetRelativePath(root, file), file)))
                    return new GlobalSearchResponse(results, true);
            }

            foreach (var subdirectory in subdirectories)
            {
                if (Path.GetFileName(subdirectory).Equals(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if ((File.GetAttributes(subdirectory) & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) == 0)
                        pending.Push(subdirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }

        return new GlobalSearchResponse(results, false);
    }

    private static bool Matches(string? value, string query)
        => value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
}

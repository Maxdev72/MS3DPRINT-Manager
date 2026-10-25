using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectDuplicationService
{
    public ProjectSummary Duplicate(string root, ProjectSummary source, ClientSummary client, string name, bool includeFiles)
    {
        var paths = new WorkspaceMetadataPaths(root);
        void Inside(string path)
        {
            var relative = Path.GetRelativePath(paths.RootPath, Path.GetFullPath(path));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Le projet et le client doivent appartenir à l’espace de travail.");
            WorkspacePathSafety.EnsureNoLinks(path);
        }
        Inside(source.ProjectPath); Inside(client.ClientPath);
        var clientPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(client.ClientPath));
        var sourceParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.ProjectPath)));
        if (!string.Equals(Path.GetFullPath(source.ClientPath), clientPath, StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(sourceParent, clientPath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sourceParent, Path.Combine(clientPath, ProjectSummary.ArchiveFolderName), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Le projet source doit appartenir au client sélectionné.");
        if (client.Profile is null) throw new InvalidOperationException("Complétez la fiche client avant de dupliquer le projet.");
        if (!Directory.Exists(source.ProjectPath)) throw new DirectoryNotFoundException("Le projet source est introuvable.");
        // Check every source before creating anything, and before descending into a directory.
        var files = new List<string>(); var directories = new List<string>();
        void Visit(string folder)
        {
            Inside(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                Inside(entry);
                if (Directory.Exists(entry)) { directories.Add(Path.GetRelativePath(source.ProjectPath, entry)); Visit(entry); }
                else files.Add(Path.GetRelativePath(source.ProjectPath, entry));
            }
        }
        if (includeFiles) Visit(source.ProjectPath);
        var store = new ProjectProfileStore(paths);
        var created = new ProjectCreationService(new FolderTreeService()).CreateWithProfile(client, DateTime.Today.Year, name,
            store, null, source.Profile?.Description, source.Priority);
        var target = Path.Combine(client.ClientPath, created.Reference.FolderName);
        try
        {
            if (source.Profile?.Notes is { } notes) store.Update(created.Profile with { Notes = notes });
            foreach (var directory in directories) { var path = Path.Combine(target, directory); Inside(path); Directory.CreateDirectory(path); }
            foreach (var file in files)
            {
                var original = Path.Combine(source.ProjectPath, file); var copy = Path.Combine(target, file);
                Inside(original); Inside(copy);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(original, copy, overwrite: false);
            }
            var profile = store.LoadReadable().Single(p => p.Id == created.Profile.Id);
            return new(client.FolderName, client.ClientPath, target, profile.Reference, profile.FolderName, profile);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            CreationRollback.Preserve(paths.RootPath, target, [Path.Combine(paths.ProjectsDirectory, created.Profile.Id + ".json")], failure);
            throw;
        }
    }
}

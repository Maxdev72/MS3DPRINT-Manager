using System.Security.Cryptography;
using System.Text;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectCreationService
{
    private readonly FolderTreeService _folders;

    public ProjectCreationService(FolderTreeService folders)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    }

    public ProjectReference Create(string clientPath, string clientCode, int year, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientPath);
        var fullClientPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(clientPath));
        var reservations = new ProjectReferenceReservations(WorkspaceRoot(fullClientPath));
        using var mutex = new Mutex(false, LockName(reservations.DirectoryPath));
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!Directory.Exists(fullClientPath))
            {
                throw new DirectoryNotFoundException("Le dossier du client sélectionné est introuvable.");
            }

            WorkspacePathSafety.EnsureNoLinks(fullClientPath);

            var existingNames = Directory.EnumerateDirectories(fullClientPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).OfType<string>().Concat(reservations.ExistingNames()).ToArray();
            var reference = ProjectReferenceGenerator.Create(clientCode, year, existingNames, projectName);
            reservations.Reserve($"{reference.ClientCode}-{reference.Year:D4}-{reference.Sequence:D3}");
            _folders.CreateTree(Path.Combine(fullClientPath, reference.FolderName), FolderTemplates.Project);
            return reference;
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static string WorkspaceRoot(string clientPath)
    {
        for (string? current = clientPath; current is not null; current = Path.GetDirectoryName(current))
            if (Path.GetFileName(current).Equals("01_CLIENTS", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(current)!;
        return clientPath;
    }

    public (ProjectReference Reference, ProjectProfile Profile) CreateWithProfile(
        ClientSummary client, int year, string projectName, ProjectProfileStore profiles, DateOnly? dueDate, string? description)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(profiles);
        if (client.Profile is null) throw new InvalidOperationException("Complétez la fiche client avant de créer un projet suivi.");

        var reference = Create(client.ClientPath, client.ClientCode, year, projectName);
        var now = DateTimeOffset.UtcNow;
        var profile = new ProjectProfile(Guid.NewGuid(), client.Profile.Id, client.ClientCode,
            $"{reference.ClientCode}-{reference.Year:D4}-{reference.Sequence:D3}", reference.FolderName,
            reference.NormalizedProjectName, ProjectStatus.Quote, now, dueDate, description, null, now,
            Path.GetRelativePath(WorkspaceRoot(client.ClientPath), Path.Combine(client.ClientPath, reference.FolderName)).Replace('\\', '/'));
        try { profiles.Create(profile); }
        catch (Exception failure)
        {
            CreationRollback.Preserve(WorkspaceRoot(client.ClientPath), Path.Combine(client.ClientPath, reference.FolderName), [], failure);
            throw;
        }
        return (reference, profile);
    }

    private static string LockName(string clientPath)
    {
        var key = OperatingSystem.IsWindows() ? clientPath.ToUpperInvariant() : clientPath;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return (OperatingSystem.IsWindows() ? @"Global\" : string.Empty) + "MS3DPRINT-PROJECT-" + hash;
    }
}

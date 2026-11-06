using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Documents;

public enum MirrorAvailability { Complete, Incomplete, Unknown }

public sealed record MirrorAnomaly(string RelativePath, string Reason);

public sealed record MirrorCheckResult(MirrorAvailability Availability, int VerifiedFiles, IReadOnlyList<MirrorAnomaly> Anomalies);

/// <summary>
/// Proves that the documents of a bound folder are really on this PC: right marker, files present, hydrated (not virtual
/// placeholders) and readable. Without the expected manifest, a clean folder is <see cref="MirrorAvailability.Unknown"/>,
/// never Complete. It never opens a placeholder (that would start a download) and never modifies the folder.
/// </summary>
public sealed class LocalDocumentMirror
{
    // Explicit exclusions only: generated content, never business files picked by extension.
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj" };
    private const FileAttributes Offline = (FileAttributes)0x1000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes Unpinned = (FileAttributes)0x100000;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private enum Probe { Ready, NotHydrated, MayBeFreed, Unreadable }

    public Task<MirrorCheckResult> CheckAsync(LocalWorkspaceBinding binding, CancellationToken token)
        => CheckAsync(binding, expectedRelativePaths: null, token);

    public Task<MirrorCheckResult> CheckAsync(LocalWorkspaceBinding binding, IReadOnlyCollection<string>? expectedRelativePaths, CancellationToken token)
        => Task.Run(() => Check(binding, expectedRelativePaths, token), token);

    /// <summary>The candidate folders whose marker designates this workspace and storage; never chosen by name alone.</summary>
    public static IReadOnlyList<string> MatchingRoots(IEnumerable<string> candidates, Guid workspaceId, Guid storageBindingId)
        => candidates.Where(root => WorkspaceDocumentMarker.Read(root)?.Matches(workspaceId, storageBindingId) == true)
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))).Distinct(PathComparer).ToArray();

    private static MirrorCheckResult Check(LocalWorkspaceBinding binding, IReadOnlyCollection<string>? expected, CancellationToken token)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binding.LocalDocumentsRoot));
        if (!Directory.Exists(root)) return new(MirrorAvailability.Incomplete, 0, [new("", "Dossier documentaire introuvable ou disque déconnecté.")]);
        var marker = WorkspaceDocumentMarker.Read(root);
        if (marker is null) return new(MirrorAvailability.Incomplete, 0, [new(WorkspaceDocumentMarker.FileName, "Marqueur d’atelier absent ou illisible.")]);
        if (!marker.Matches(binding.WorkspaceId, binding.StorageBindingId))
            return new(MirrorAvailability.Incomplete, 0, [new(WorkspaceDocumentMarker.FileName, "Ce dossier appartient à un autre atelier.")]);

        var anomalies = new List<MirrorAnomaly>();
        var mayBeFreed = false;
        var verified = 0;
        var seen = new HashSet<string>(PathComparer);
        foreach (var file in Enumerate(root, anomalies, token))
        {
            token.ThrowIfCancellationRequested();
            var relative = Relative(root, file);
            seen.Add(relative);
            switch (ProbeFile(file))
            {
                case Probe.Ready: verified++; break;
                case Probe.MayBeFreed: verified++; mayBeFreed = true; break;
                case Probe.NotHydrated: anomalies.Add(new(relative, "Fichier non téléchargé sur ce PC.")); break;
                default: anomalies.Add(new(relative, "Fichier illisible.")); break;
            }
        }
        foreach (var path in expected ?? [])
        {
            var normalized = path.Replace('\\', '/');
            var full = Path.IsPathRooted(normalized) ? null : Path.GetFullPath(Path.Combine(root, normalized));
            if (full is null || !full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
                anomalies.Add(new(path, "Chemin hors du dossier de l’atelier."));
            else if (!seen.Contains(Relative(root, full)))
                anomalies.Add(new(normalized, "Document attendu absent de ce PC."));
        }
        var availability = anomalies.Count > 0 ? MirrorAvailability.Incomplete
            : mayBeFreed || expected is null ? MirrorAvailability.Unknown : MirrorAvailability.Complete;
        return new(availability, verified, anomalies);
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> Enumerate(string root, List<MirrorAnomaly> anomalies, CancellationToken token)
    {
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            string[] files, directories;
            try { files = Directory.GetFiles(current); directories = Directory.GetDirectories(current); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { anomalies.Add(new(Relative(root, current), "Dossier illisible.")); continue; }
            foreach (var file in files)
                if (!string.Equals(Path.GetFileName(file), WorkspaceDocumentMarker.FileName, StringComparison.OrdinalIgnoreCase)) yield return file;
            foreach (var child in directories)
            {
                if (ExcludedDirectories.Contains(Path.GetFileName(child))) continue;
                // Symbolic links and junctions are not followed: they could leave the workspace.
                if (new DirectoryInfo(child).LinkTarget is not null) { anomalies.Add(new(Relative(root, child), "Lien non suivi.")); continue; }
                pending.Push(child);
            }
        }
    }

    private static Probe ProbeFile(string file)
    {
        try
        {
            var info = new FileInfo(file);
            // Checked before opening: opening a placeholder would start its download.
            if ((info.Attributes & (Offline | RecallOnOpen | RecallOnDataAccess)) != 0) return Probe.NotHydrated;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1))
                if (info.Length > 0 && stream.ReadByte() < 0) return Probe.Unreadable;
            return (info.Attributes & Unpinned) != 0 ? Probe.MayBeFreed : Probe.Ready;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Probe.Unreadable; }
    }
}

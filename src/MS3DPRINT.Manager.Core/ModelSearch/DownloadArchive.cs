using System.IO.Compression;

namespace MS3DPRINT.Manager.Core.ModelSearch;

/// <summary>What a downloaded ZIP held: the 3D files taken out of it, or why nothing could be taken.</summary>
public sealed record ArchiveContent(IReadOnlyList<BufferedFile> Files, string? Problem);

/// <summary>
/// Opens a downloaded ZIP without the user seeing it: only the 3D files are kept, flat in the target folder (so no path of the
/// archive can lead outside it), and the size is counted while copying, not trusted from the archive's headers.
/// </summary>
public static class DownloadArchive
{
    public const int MaxEntries = 5000;
    public const long MaxEntryBytes = 1L * 1024 * 1024 * 1024;
    public const long MaxTotalBytes = 2L * 1024 * 1024 * 1024;

    public static ArchiveContent ExtractModels(string zipPath, string targetFolder, CancellationToken cancellationToken = default)
    {
        var kept = new List<BufferedFile>();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > MaxEntries) return new ArchiveContent([], "Cette archive contient trop de fichiers pour être ouverte automatiquement.");

            Directory.CreateDirectory(targetFolder);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.Name; // the file name alone: folders of the archive are dropped
                if (name.Length == 0 || name.StartsWith("._", StringComparison.Ordinal) || entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!DownloadBuffer.IsModelFile(name)) continue;
                if (entry.Length > MaxEntryBytes || total + entry.Length > MaxTotalBytes)
                    return Fail(kept, "Cette archive est trop volumineuse pour être ouverte automatiquement.");

                var destination = UniquePath(targetFolder, SafeName(name));
                long written;
                using (var source = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    written = Copy(source, output, Math.Min(MaxEntryBytes, MaxTotalBytes - total), cancellationToken);
                if (written < 0) { TryDelete(destination); return Fail(kept, "Cette archive est trop volumineuse pour être ouverte automatiquement."); }
                total += written;
                kept.Add(new BufferedFile(destination, written));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return Fail(kept, exception is InvalidDataException or NotSupportedException
                ? "Cette archive est protégée par un mot de passe ou illisible."
                : "L’archive n’a pas pu être ouverte : " + exception.Message);
        }
        return kept.Count == 0 ? new ArchiveContent([], "Cette archive ne contient aucun fichier 3D (STL, 3MF, OBJ, STEP, AMF).") : new ArchiveContent(kept, null);
    }

    /// <summary>Copies at most <paramref name="limit"/> bytes; -1 when the stream holds more (a header that lied).</summary>
    private static long Copy(Stream source, Stream output, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > limit) return -1;
            output.Write(buffer, 0, read);
        }
        return total;
    }

    private static ArchiveContent Fail(List<BufferedFile> kept, string problem)
    {
        foreach (var file in kept) TryDelete(file.Path);
        return new ArchiveContent([], problem);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        return clean.Length == 0 ? "modele" + Path.GetExtension(name) : clean;
    }

    private static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
        return path;
    }
}

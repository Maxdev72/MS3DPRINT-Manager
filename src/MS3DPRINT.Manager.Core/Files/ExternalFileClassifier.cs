using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Files;

public enum SlicerKind { BambuStudio, OrcaSlicer }

/// <summary>What a 3MF says about the program that wrote it and the models it was built from.</summary>
public sealed record ThreeMfInspection(SlicerKind Slicer, string? ApplicationVersion, IReadOnlyList<string> ModelNames)
{
    public string SlicerName => Slicer == SlicerKind.BambuStudio ? "Bambu Studio" : "OrcaSlicer";
}

public static class ThreeMfInspector
{
    private const int MaxEntryBytes = 8 * 1024 * 1024;
    private static readonly Regex ApplicationPattern = new(@"<metadata\s+name=""Application"">\s*([^<]+?)\s*</metadata>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex NamePattern = new(@"<metadata\s+key=""name""\s+value=""([^""]*)""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Null when the file is not a readable 3MF written by a recognised slicer.</summary>
    public static ThreeMfInspection? Inspect(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var model = ReadEntry(archive, "3D/3dmodel.model");
            if (model is null) return null;
            var application = ApplicationPattern.Match(model).Groups[1].Value;
            SlicerKind slicer;
            if (application.StartsWith("BambuStudio", StringComparison.OrdinalIgnoreCase)) slicer = SlicerKind.BambuStudio;
            else if (application.StartsWith("OrcaSlicer", StringComparison.OrdinalIgnoreCase)) slicer = SlicerKind.OrcaSlicer;
            else return null;
            var version = application.Contains('-') ? application[(application.IndexOf('-') + 1)..] : null;
            var settings = ReadEntry(archive, "Metadata/model_settings.config") ?? "";
            var names = NamePattern.Matches(settings).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new(slicer, version, names);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { return null; }
    }

    private static string? ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null || entry.Length > MaxEntryBytes) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>A file found in the project that a slicer wrote, with where it should go. Nothing is moved until the user confirms.</summary>
public sealed record ClassificationProposal(string FilePath, string RelativePath, string SlicerName, string? SourceModel, ProjectFileDestination Destination, string Reason);

public static class ExternalFileClassifier
{
    public static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(15);
    private const int MaxFilesVisited = 5000;

    /// <param name="ignored">Project-relative paths the user chose to ignore.</param>
    public static IReadOnlyList<ClassificationProposal> Scan(string projectPath, string projectFolderName, ProjectTemplate template, IReadOnlySet<string>? ignored = null, DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderName);
        ArgumentNullException.ThrowIfNull(template);
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        if (!Directory.Exists(project)) return [];
        if (!ProjectReferenceFormat.TryParseFolderName(projectFolderName, out var reference)) return [];
        WorkspacePathSafety.EnsureNoLinks(project);
        var clock = now ?? DateTimeOffset.UtcNow;
        var targetRelative = template.DirectoryFor(ProjectFileCategory.ThreeMf);
        var targetFull = Path.Combine(project, targetRelative);

        var stlStems = new List<(string FileName, string Stem)>();
        var candidates = new List<string>();
        var visited = 0;
        var pending = new Stack<string>(); pending.Push(project);
        while (pending.Count > 0 && visited < MaxFilesVisited)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (++visited > MaxFilesVisited) break;
                var name = Path.GetFileName(entry);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0 && (attributes & FileAttributes.Directory) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (name.StartsWith('.') || name.Equals("99_ARCHIVES", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(Path.GetFullPath(entry), Path.GetFullPath(targetFull), StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(entry);
                    continue;
                }
                var extension = Path.GetExtension(name);
                if (extension.Equals(".stl", StringComparison.OrdinalIgnoreCase)) stlStems.Add((name, StripReference(Path.GetFileNameWithoutExtension(name))));
                else if (extension.Equals(".3mf", StringComparison.OrdinalIgnoreCase) && !name.StartsWith('~')) candidates.Add(entry);
            }
        }

        var proposals = new List<ClassificationProposal>();
        foreach (var file in candidates)
        {
            var relative = Path.GetRelativePath(project, file);
            if (ignored is not null && ignored.Contains(relative.Replace('\\', '/'))) continue;
            if (!IsSettled(file, clock)) continue;
            var inspection = ThreeMfInspector.Inspect(file);
            if (inspection is null) continue;
            var source = FindSource(inspection.ModelNames, stlStems);
            var fileName = ProjectFileClassifier.SuggestImportName(Path.GetFileName(file), reference.ToString());
            fileName = FreeName(targetFull, fileName);
            var version = inspection.ApplicationVersion is { } v ? " " + string.Join('.', v.Split('.').Take(2)) : "";
            var reason = source is null ? $"Projet {inspection.SlicerName}{version}, aucun STL du projet reconnu."
                : $"Projet {inspection.SlicerName}{version}, modèle source : {source}.";
            proposals.Add(new(file, relative, inspection.SlicerName, source, new ProjectFileDestination(ProjectFileCategory.ThreeMf, targetRelative, fileName), reason));
        }
        return proposals.OrderBy(p => p.RelativePath, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>Not touched for a few seconds and openable without sharing: the program that wrote it is done with it.</summary>
    private static bool IsSettled(string path, DateTimeOffset now)
    {
        try
        {
            if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) < StableAfter) return false;
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return probe.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? FindSource(IReadOnlyList<string> modelNames, List<(string FileName, string Stem)> stls)
    {
        foreach (var model in modelNames)
        {
            var cut = model.IndexOf(".stl", StringComparison.OrdinalIgnoreCase);
            if (cut <= 0) continue;
            var stem = StripReference(model[..cut]);
            var match = stls.FirstOrDefault(stl => string.Equals(stl.Stem, stem, StringComparison.CurrentCultureIgnoreCase));
            if (match.FileName is not null) return match.FileName;
        }
        return null;
    }

    private static string StripReference(string stem)
    {
        var marker = stem.LastIndexOf("__", StringComparison.Ordinal);
        return marker > 0 ? stem[..marker] : stem;
    }

    private static string FreeName(string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName)) && !Directory.Exists(Path.Combine(directory, fileName))) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName); var extension = Path.GetExtension(fileName);
        for (var attempt = 2; attempt < 1000; attempt++)
        {
            var candidate = $"{stem} ({attempt}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate))) return candidate;
        }
        return fileName;
    }
}

/// <summary>Remembers the proposals the user declined, so they are not offered again.</summary>
public sealed class IgnoredClassificationStore
{
    private readonly string _path;
    public IgnoredClassificationStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "classification-ignored.json");
    }

    /// <summary>Workspace-relative paths with '/' separators.</summary>
    public IReadOnlySet<string> Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var items = JsonSerializer.Deserialize<string[]>(stream) ?? [];
            return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public void Add(string workspaceRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRelativePath);
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var mutation = ProfileMutationLock.Acquire(_path);
        var all = new HashSet<string>(Load(), StringComparer.OrdinalIgnoreCase) { workspaceRelativePath.Replace('\\', '/') };
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, all.Order().ToArray()); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

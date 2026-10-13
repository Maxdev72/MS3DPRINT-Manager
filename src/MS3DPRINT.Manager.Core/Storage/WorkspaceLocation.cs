using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Storage;

/// <summary>
/// Where the workspace lives: the <c>MS3DPRINT_WORKSPACE</c> environment variable (tests, demos) wins, then the folder chosen in
/// the settings and kept in <c>data/workspace.json</c>, then the historical default.
/// </summary>
public sealed class WorkspaceLocation
{
    public const string EnvironmentVariable = "MS3DPRINT_WORKSPACE";
    public const string DefaultRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";

    private sealed record Stored(string? Root);

    private readonly string _dataDirectory;
    private readonly string _path;

    public WorkspaceLocation(string? dataDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
        _path = Path.Combine(_dataDirectory, "workspace.json");
    }

    /// <summary>True when the environment variable fixes the workspace, so the settings cannot change it.</summary>
    public static bool IsFixedByEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public string Resolve()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnvironment && !string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;
        return ReadStored() ?? DefaultRoot;
    }

    public void Save(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Ce dossier n’existe pas.");
        Directory.CreateDirectory(_dataDirectory);
        var temporary = Path.Combine(_dataDirectory, "workspace." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Stored(full), new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string? ReadStored()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var root = JsonSerializer.Deserialize<Stored>(stream)?.Root;
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) ? root : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}

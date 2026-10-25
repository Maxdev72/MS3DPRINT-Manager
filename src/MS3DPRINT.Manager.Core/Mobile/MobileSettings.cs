using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Mobile;

/// <summary>Whether the computer publishes the picture of the workshop for the phone. On by default; the user can switch it off.</summary>
public sealed record MobileSettings(bool PublishEnabled = true);

public sealed class MobileSettingsStore
{
    private readonly string _path;
    public MobileSettingsStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "mobile-settings.json");
    }

    public MobileSettings Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return new();
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<MobileSettings>(stream) ?? new();
        }
        catch (JsonException) { return new(); }
    }

    public void Save(MobileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var mutation = ProfileMutationLock.Acquire(_path);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

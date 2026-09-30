namespace MS3DPRINT.Manager.Core.Files;

public sealed record ProjectFileEntry(string Name, string FullPath, bool IsDirectory, long? Length, DateTimeOffset LastWriteTime)
{
    public string Icon => IsDirectory ? "📁" : "📄";
}

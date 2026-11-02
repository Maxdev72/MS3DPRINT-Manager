namespace MS3DPRINT.Manager.Core.Migration;

public sealed record MigrationFile(string Path, long Bytes, string Sha256, DateTimeOffset ModifiedAt);
public sealed record MigrationIssue(string Code, string Path, string Message, bool Blocking = true);

/// <summary>A favourable report permits review of a prepared copy, never automatic activation.</summary>
public sealed record MigrationReport(int Version, string Operation, bool Succeeded, DateTimeOffset CreatedAt,
    string? ArchiveSha256, IReadOnlyList<MigrationFile> Files, IReadOnlyList<string> Directories, IReadOnlyList<MigrationIssue> Issues);

namespace MS3DPRINT.Manager.Core.Migration;

public enum WorkspaceDifferenceKind { Same, PcOnly, ServerOnly, Conflict }
public enum WorkspaceConflictChoice { Pc, Server }
public sealed record WorkspaceSnapshot(string Fingerprint, IReadOnlyList<MigrationFile> Files, IReadOnlyList<string> Directories,
    IReadOnlyList<MigrationIssue> Issues, IReadOnlyDictionary<string, string>? Labels = null);
public sealed record WorkspaceDifference(string Path, WorkspaceDifferenceKind Kind, MigrationFile? Pc, MigrationFile? Server, string DisplayName);
public sealed record WorkspaceReconciliationPlan(string PcFingerprint, string ServerFingerprint, string PlanHash,
    IReadOnlyList<WorkspaceDifference> Files, IReadOnlyList<string> Directories, IReadOnlyList<MigrationIssue> Issues);
public sealed record WorkspaceReconciliationChoice(string Path, WorkspaceConflictChoice Choice);
public sealed record WorkspaceVersionChange(string Path, DateTimeOffset PreviousUpdatedAt, DateTimeOffset UpdatedAt, string BeforeSha256, string AfterSha256);
public sealed record WorkspaceReconciliationCandidate(bool Succeeded, WorkspaceSnapshot Snapshot,
    IReadOnlyList<WorkspaceVersionChange> VersionChanges, IReadOnlyList<MigrationIssue> Issues);

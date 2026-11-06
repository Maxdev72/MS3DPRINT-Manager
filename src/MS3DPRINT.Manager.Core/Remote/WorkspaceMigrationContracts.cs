using MS3DPRINT.Manager.Core.Migration;

namespace MS3DPRINT.Manager.Core.Remote;

public enum WorkspaceMigrationPhase { Uploading, Uploaded, Preparing, Prepared, Applying, Applied, Blocked }
/// <summary>Private workspace transfer limits.</summary>
public sealed record WorkspaceMigrationCapabilities(int Version, int ChunkBytes, long MaxArchiveBytes);
/// <summary>Begins or resumes an upload with the same immutable archive identity.</summary>
public sealed record StartWorkspaceMigration(Guid CommandId, long Bytes, string Sha256);
/// <summary>Durable activation proof without private server paths.</summary>
public sealed record WorkspaceMigrationReceipt(Guid OperationId, Guid CommandId, string Fingerprint,
    DateTimeOffset AppliedAt, string PcFingerprint, string ServerBeforeFingerprint, bool BackupVerified);
/// <summary>Safe resumable operation status.</summary>
public sealed record WorkspaceMigrationStatus(Guid OperationId, WorkspaceMigrationPhase Phase, long ArchiveBytes,
    string ArchiveSha256, int ChunkBytes, int[] ReceivedParts, WorkspaceReconciliationPlan? Plan,
    IReadOnlyList<WorkspaceReconciliationChoice> Choices, string? ReviewHash, string? CandidateFingerprint,
    WorkspaceMigrationReceipt? Receipt, IReadOnlyList<MigrationIssue> Issues);
/// <summary>Explicit confirmation tied to the reviewed candidate.</summary>
public sealed record ApplyWorkspaceMigration(Guid CommandId, string ReviewHash, bool Confirmed);

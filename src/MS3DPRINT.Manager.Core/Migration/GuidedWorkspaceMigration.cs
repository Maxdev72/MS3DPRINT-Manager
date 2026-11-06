using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;
namespace MS3DPRINT.Manager.Core.Migration;

public sealed class GuidedWorkspaceMigrationIdentityException() : InvalidOperationException("Cette reprise appartient à un autre atelier, serveur ou compte.");

public sealed record GuidedWorkspaceMigrationJournal(string Server, string Account, string SourceRoot, string SourceFingerprint,
    string Directory, Guid CommandId, Guid? OperationId, string? ArchiveSha256, long ArchiveBytes, bool ApplyRequested,
    Guid? ApplyCommandId, string? ReviewHash, string? CandidateFingerprint, string? ServerBeforeFingerprint, string? PcFingerprint,
    WorkspaceMigrationReceipt? Receipt)
{ public string? PreparationName { get; init; } public string? OriginalArchiveSha256 { get; init; } public IReadOnlyList<string> ExcludedEntries { get; init; } = []; }

/// <summary>Private local recovery state; credentials are never serialized. Apply intent is durable before sending.</summary>
public sealed class GuidedWorkspaceMigration : IDisposable
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public GuidedWorkspaceMigrationJournal Journal { get; private set; }
    public string JournalPath => Path.Combine(Journal.Directory, "journal.json");
    public string ArchivePath => Path.Combine(Journal.Directory, Journal.PreparationName ?? "portable", "portable.zip");
    public PortableMigrationReport? Preparation { get; private set; }
    private readonly FileStream _lease;
    private readonly object _state = new();
    private bool _disposed;
    private GuidedWorkspaceMigration(GuidedWorkspaceMigrationJournal journal)
    {
        Journal = journal;
        var leasePath = Path.Combine(journal.Directory, "operation.lock"); WorkspacePathSafety.EnsureNoLinks(leasePath);
        _lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() { lock (_state) { _disposed = true; _lease.Dispose(); } }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
    public static GuidedWorkspaceMigration Create(Uri server, string account, string source, string privateParent)
    {
        _ = new RemoteDeviceSession(server.AbsoluteUri, account, "validation", DateTimeOffset.UtcNow);
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        privateParent = Path.GetFullPath(privateParent);
        if (Inside(privateParent, source) || privateParent.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("Nextcloud", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("La reprise exige un emplacement privé hors atelier et Nextcloud.");
        WorkspacePathSafety.EnsureNoLinks(source); WorkspacePathSafety.EnsureNoLinks(privateParent);
        var snapshot = new WorkspaceReconciliationService().Capture(source);
        var directory = Path.Combine(privateParent, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var migration = new GuidedWorkspaceMigration(new(server.AbsoluteUri, account.Trim(), source, snapshot.Fingerprint, directory,
            Guid.NewGuid(), null, null, 0, false, null, null, null, null, null, null)
            { ExcludedEntries = snapshot.Issues.Where(i => i.Code == "ExcludedEntry").Select(i => i.Path).Order(StringComparer.Ordinal).ToArray() });
        migration.Save(); return migration;
    }
    public static GuidedWorkspaceMigration Load(string path, Uri server, string account, string source)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Journal trop volumineux.");
        var journal = JsonSerializer.Deserialize<GuidedWorkspaceMigrationJournal>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException();
        if (string.IsNullOrWhiteSpace(journal.Server) || string.IsNullOrWhiteSpace(journal.Account)
            || string.IsNullOrWhiteSpace(journal.SourceRoot) || string.IsNullOrWhiteSpace(journal.Directory)
            || journal.SourceFingerprint is not { Length: 64 } || !journal.SourceFingerprint.All(Uri.IsHexDigit)
            || journal.ExcludedEntries is null || journal.ExcludedEntries.Any(p => string.IsNullOrWhiteSpace(p))
            || journal.ApplyRequested && (journal.OperationId is null || journal.ApplyCommandId is null || journal.ApplyCommandId == Guid.Empty
                || journal.ReviewHash is not { Length: 64 } || journal.CandidateFingerprint is not { Length: 64 }
                || journal.PcFingerprint is not { Length: 64 } || journal.ServerBeforeFingerprint is not { Length: 64 })
            || journal.Receipt is not null && !journal.ApplyRequested
            || journal.ArchiveSha256 is not null && (journal.ArchiveSha256.Length != 64 || !journal.ArchiveSha256.All(Uri.IsHexDigit)
                || journal.ArchiveBytes is <= 0 or > 4294967296 || journal.PreparationName is null || journal.OriginalArchiveSha256 is not { Length: 64 }))
            throw new InvalidDataException("Journal de reprise incomplet : aucune nouvelle activation autorisée.");
        _ = new RemoteDeviceSession(journal.Server, journal.Account, "validation", DateTimeOffset.UtcNow);
        if (new Uri(journal.Server) != server || !journal.Account.Equals(account.Trim(), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(journal.SourceRoot).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), PathComparison)
            )
            throw new GuidedWorkspaceMigrationIdentityException();
        if (Path.GetFullPath(path) != Path.Combine(journal.Directory, "journal.json") || journal.CommandId == Guid.Empty
            || journal.SourceFingerprint.Length != 64 || journal.OperationId is { } id && id != journal.CommandId
            || journal.PreparationName is { } name && (!name.StartsWith("portable-", StringComparison.Ordinal) || !Guid.TryParseExact(name[9..], "N", out _)))
            throw new InvalidDataException("Journal de reprise invalide : aucune nouvelle activation autorisée.");
        WorkspacePathSafety.EnsureNoLinks(journal.Directory);
        return new(journal);
    }
    public bool SourceStable()
    {
        ThrowIfDisposed();
        if (!Directory.Exists(Journal.SourceRoot)) return false;
        var snapshot = new WorkspaceReconciliationService().Capture(Journal.SourceRoot);
        return snapshot.Fingerprint == Journal.SourceFingerprint && Journal.ExcludedEntries is not null
            && snapshot.Issues.Where(i => i.Code == "ExcludedEntry").Select(i => i.Path).Order(StringComparer.Ordinal).SequenceEqual(Journal.ExcludedEntries);
    }
    public void RequireStableSource() { if (!SourceStable()) throw new InvalidDataException("Les données PC ont changé. Conservez cette sauvegarde et préparez une nouvelle reprise."); }
    public PortableMigrationReport Prepare(IReadOnlySet<Guid> inactiveMissingCollections)
    {
        lock (_state) { ThrowIfDisposed(); return PrepareCore(inactiveMissingCollections); }
    }
    private PortableMigrationReport PrepareCore(IReadOnlySet<Guid> inactiveMissingCollections)
    {
        if (Journal.OperationId is not null || Journal.ApplyRequested) throw new InvalidOperationException("La préparation transférée ne peut plus changer.");
        RequireStableSource();
        var backup = new WorkspaceBackupService(); var original = Path.Combine(Journal.Directory, "original.zip");
        if (!File.Exists(original))
        {
            var result = backup.Create(Journal.SourceRoot, original, true);
            Journal = Journal with { OriginalArchiveSha256 = result.ArchiveSha256 }; Save();
        }
        if (Journal.OriginalArchiveSha256 is null || backup.Inspect(original).ArchiveSha256 != Journal.OriginalArchiveSha256)
            throw new InvalidDataException("La sauvegarde originale ne correspond plus à son empreinte.");
        var verified = Path.Combine(Journal.Directory, "original-verified-" + Guid.NewGuid().ToString("N"));
        backup.Restore(original, verified, Journal.OriginalArchiveSha256);
        if (new WorkspaceReconciliationService().Capture(verified).Fingerprint != Journal.SourceFingerprint)
            throw new InvalidDataException("La sauvegarde ne correspond pas à la source.");
        RequireStableSource();
        var preparationName = "portable-" + Guid.NewGuid().ToString("N");
        var destination = Path.Combine(Journal.Directory, preparationName);
        Preparation = new PortableWorkspaceMigrationService().Prepare(original, destination, backup.Inspect(original).ArchiveSha256, inactiveMissingCollections);
        if (Preparation.Succeeded)
            Journal = Journal with { ArchiveSha256 = Preparation.PortableArchiveSha256, ArchiveBytes = new FileInfo(Path.Combine(destination, "portable.zip")).Length, PreparationName = preparationName };
        RequireStableSource(); Save(); return Preparation;
    }
    public async Task<WorkspaceMigrationStatus> TransferAsync(RemoteWorkspaceClient client, IProgress<long>? progress, CancellationToken token)
    {
        RequireClient(client);
        if (Journal.ApplyRequested) return await CheckAsync(client, token);
        var capabilities = await client.GetMigrationCapabilitiesAsync(token);
        RequireStableSource();
        if (Journal.ArchiveSha256 is null || Journal.ArchiveBytes <= 0 || Journal.ArchiveBytes > capabilities.MaxArchiveBytes) throw new InvalidDataException("Préparez une archive portable valide dans les limites serveur.");
        var status = Journal.OperationId is { } id ? await client.GetMigrationStatusAsync(id, token) : await client.StartMigrationAsync(new(Journal.CommandId, Journal.ArchiveBytes, Journal.ArchiveSha256), token);
        ThrowIfDisposed();
        if (status.OperationId != Journal.CommandId) throw new InvalidDataException("Identité de reprise inattendue.");
        Journal = Journal with { OperationId = status.OperationId }; Save(); ValidateStatus(status);
        if (status.Phase is WorkspaceMigrationPhase.Uploading or WorkspaceMigrationPhase.Uploaded)
        {
            status = await client.UploadMigrationAsync(ArchivePath, status, progress, token);
            ThrowIfDisposed();
            status = await client.PrepareMigrationAsync(status.OperationId, token);
        }
        ThrowIfDisposed(); ValidateStatus(status);
        return status;
    }
    public async Task<WorkspaceMigrationStatus> ApplyAsync(RemoteWorkspaceClient client, WorkspaceMigrationStatus reviewed, CancellationToken token)
    {
        RequireClient(client);
        if (Journal.ApplyRequested) return await CheckAsync(client, token);
        RequireStableSource(); ValidateStatus(reviewed);
        if (reviewed.Phase != WorkspaceMigrationPhase.Prepared || reviewed.Plan is null || reviewed.Issues.Any(i => i.Blocking)
            || string.IsNullOrEmpty(reviewed.CandidateFingerprint) || string.IsNullOrEmpty(reviewed.ReviewHash)
            || new WorkspaceReconciliationService().ReviewHash(reviewed.Plan, reviewed.Choices) != reviewed.ReviewHash)
            throw new InvalidDataException("Le bilan doit être complet et vérifié avant activation.");
        Journal = Journal with { ApplyRequested = true, ApplyCommandId = Guid.NewGuid(), ReviewHash = reviewed.ReviewHash,
            CandidateFingerprint = reviewed.CandidateFingerprint, ServerBeforeFingerprint = reviewed.Plan.ServerFingerprint, PcFingerprint = reviewed.Plan.PcFingerprint };
        Save();
        var status = await client.ApplyMigrationAsync(reviewed.OperationId, new(Journal.ApplyCommandId.Value, reviewed.ReviewHash, true), token);
        return VerifyReceipt(status);
    }
    public async Task<WorkspaceMigrationStatus> CheckAsync(RemoteWorkspaceClient client, CancellationToken token)
    { RequireClient(client); return VerifyReceipt(await client.GetMigrationStatusAsync(Journal.OperationId ?? throw new InvalidDataException(), token)); }
    private WorkspaceMigrationStatus VerifyReceipt(WorkspaceMigrationStatus status)
    {
        ThrowIfDisposed();
        ValidateStatus(status);
        if (status.Phase == WorkspaceMigrationPhase.Applied)
        {
            var receipt = status.Receipt;
            if (!Journal.ApplyRequested || receipt is null || receipt.OperationId != Journal.OperationId || receipt.CommandId != Journal.ApplyCommandId
                || !receipt.BackupVerified || receipt.Fingerprint != Journal.CandidateFingerprint || receipt.PcFingerprint != Journal.PcFingerprint
                || receipt.ServerBeforeFingerprint != Journal.ServerBeforeFingerprint || receipt.AppliedAt == default)
                throw new InvalidDataException("Reçu final incompatible : aucune bascule de mode autorisée.");
            Journal = Journal with { Receipt = receipt }; Save();
        }
        return status;
    }
    private void ValidateStatus(WorkspaceMigrationStatus status)
    {
        if (status.ReceivedParts is null || status.Choices is null || status.Issues is null || status.ChunkBytes != 524288 || status.OperationId != Journal.OperationId || status.ArchiveBytes != Journal.ArchiveBytes || !string.Equals(status.ArchiveSha256, Journal.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’état distant ne correspond pas à cette reprise.");
    }
    private void RequireClient(RemoteWorkspaceClient client)
    {
        ThrowIfDisposed();
        if (new Uri(client.CaptureSession(Journal.Account).Server) != new Uri(Journal.Server))
            throw new GuidedWorkspaceMigrationIdentityException();
    }
    private void Save()
    {
        lock (_state) { ThrowIfDisposed(); SaveCore(); }
    }
    private void SaveCore()
    {
        WorkspacePathSafety.EnsureNoLinks(JournalPath);
        var temporary = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, Journal, Json); output.Flush(true); }
        File.Move(temporary, JournalPath, true);
    }
    private static bool Inside(string path, string root) => path.Equals(root, PathComparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);
}

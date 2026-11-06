using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Server.Workspace;

namespace MS3DPRINT.Manager.Server.Migration;

public sealed class WorkspaceMigrationStore(ServerStorage storage, CommandStore commands)
{
    public const int ChunkBytes = 524288;
    public const long MaxArchiveBytes = 4294967296;
    public const int ChoicesBodyBytes = 4 * 1024 * 1024;
    private const int MaxChoices = 10000;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly WorkspaceReconciliationService _reconciliation = new();
    private string Root => Path.Combine(storage.StateRoot, "workspace-migrations");
    private string Operation(Guid id) => Path.Combine(Root, id.ToString("N"));
    private string State(Guid id) => Path.Combine(Operation(id), "state.json");
    internal static string WorkspaceIdentity(string workspace) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? Path.GetFullPath(workspace).ToUpperInvariant() : Path.GetFullPath(workspace))));
    internal static string Transaction(string root, Guid id) => Path.Combine(Path.GetDirectoryName(root)!, "." + Path.GetFileName(root) + "-migration-" + id.ToString("N"));
    internal static bool HashValid(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static void PrivateDirectory(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    internal static T Load<T>(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var input = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(input, CommandStore.Json) ?? throw new InvalidDataException();
    }
    internal static void Save<T>(string path, T value)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, value, CommandStore.Json); output.Flush(true); }
        File.Move(temporary, path, true);
    }
    public async Task<IResult> Locked(Guid id, Func<Task<IResult>> operation, CancellationToken token)
    {
        if (id == Guid.Empty) return Results.BadRequest();
        var gate = _gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync(token);
        try
        {
            if (File.Exists(State(id)) && Load<string>(Path.Combine(Operation(id), "workspace-identity.json")) != WorkspaceIdentity(storage.WorkspaceRoot)) return Results.Conflict();
            return await operation();
        }
        catch (FileNotFoundException) { return Results.NotFound(); }
        catch (DirectoryNotFoundException) { return Results.NotFound(); }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        { return Results.Conflict(new { error = "Les vérifications ont échoué. Aucune nouvelle activation n’est autorisée. Consultez l’état de la reprise." }); }
        finally { gate.Release(); }
    }
    public IResult Start(StartWorkspaceMigration input)
    {
        if (input.CommandId == Guid.Empty || input.Bytes <= 0 || input.Bytes > MaxArchiveBytes || !HashValid(input.Sha256)) return Results.BadRequest();
        if (File.Exists(State(input.CommandId)))
        {
            var previous = Load<WorkspaceMigrationStatus>(State(input.CommandId));
            return previous.ArchiveBytes == input.Bytes && previous.ArchiveSha256.Equals(input.Sha256, StringComparison.OrdinalIgnoreCase)
                ? Results.Ok(previous) : Results.Conflict();
        }
        PrivateDirectory(Root); PrivateDirectory(Operation(input.CommandId));
        Save(Path.Combine(Operation(input.CommandId), "workspace-identity.json"), WorkspaceIdentity(storage.WorkspaceRoot));
        var status = new WorkspaceMigrationStatus(input.CommandId, WorkspaceMigrationPhase.Uploading, input.Bytes, input.Sha256.ToUpperInvariant(), ChunkBytes, [], null, [], null, null, null, []);
        Save(State(input.CommandId), status);
        return Results.Created($"/api/v1/workspace-migrations/operations/{input.CommandId}", status);
    }
    public IResult Status(Guid id) => Results.Ok(Load<WorkspaceMigrationStatus>(State(id)));
    public async Task<IResult> Part(Guid id, int index, HttpRequest request, CancellationToken token)
    {
        if (request.ContentLength > ChunkBytes) return Results.StatusCode(413);
        var status = Load<WorkspaceMigrationStatus>(State(id));
        var count = (int)((status.ArchiveBytes + ChunkBytes - 1) / ChunkBytes);
        if (index < 0 || index >= count || !HashValid(request.Headers["X-MS3DFORGE-SHA256"])) return Results.BadRequest();
        if (status.Phase is not (WorkspaceMigrationPhase.Uploading or WorkspaceMigrationPhase.Uploaded)) return Results.Conflict();
        var expected = (int)Math.Min(ChunkBytes, status.ArchiveBytes - (long)index * ChunkBytes);
        var bytes = new byte[ChunkBytes + 1]; var length = 0;
        while (length < bytes.Length)
        { var read = await request.Body.ReadAsync(bytes.AsMemory(length), token); if (read == 0) break; length += read; }
        if (length > ChunkBytes) return Results.StatusCode(413);
        if (length != expected) return Results.BadRequest();
        var hash = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, length)));
        if (!hash.Equals(request.Headers["X-MS3DFORGE-SHA256"], StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
        var part = Path.Combine(Operation(id), index + ".part"); WorkspacePathSafety.EnsureNoLinks(part);
        if (File.Exists(part))
        {
            using var existing = File.OpenRead(part);
            if (existing.Length != length || Convert.ToHexString(SHA256.HashData(existing)) != hash) return Results.Conflict();
        }
        else
        {
            var temp = part + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await output.WriteAsync(bytes.AsMemory(0, length), token); output.Flush(true); }
            File.Move(temp, part);
        }
        var received = status.ReceivedParts.Append(index).Distinct().Order().ToArray();
        status = status with { ReceivedParts = received, Phase = received.Length == count ? WorkspaceMigrationPhase.Uploaded : WorkspaceMigrationPhase.Uploading };
        Save(State(id), status); return Results.Ok(status);
    }
    public IResult Prepare(Guid id)
    {
        var status = Load<WorkspaceMigrationStatus>(State(id));
        if (status.Phase == WorkspaceMigrationPhase.Prepared) return Results.Ok(status);
        if (status.Phase == WorkspaceMigrationPhase.Blocked && status.Receipt is null && status.Plan is not null
            && status.Issues.Count > 0 && status.Issues.All(i => i.Code == "ProjectProfileMissing")
            && !File.Exists(Path.Combine(Operation(id), "swap.json")))
            return commands.Read(() =>
            {
                if (commands.HasPending()) return Block(status, [new("PendingCommands", "", "Une commande métier a un résultat incertain.")]);
                var pc = _reconciliation.Capture(Path.Combine(Operation(id), "stage", "workspace"));
                var before = _reconciliation.Capture(Path.Combine(Operation(id), "server"));
                if (pc.Fingerprint != status.Plan.PcFingerprint || before.Fingerprint != status.Plan.ServerFingerprint
                    || _reconciliation.Capture(storage.WorkspaceRoot).Fingerprint != before.Fingerprint)
                    return Block(status, [new("ServerChanged", "", "Les données ont changé depuis cette comparaison. Préparez une nouvelle reprise.")]);
                var plan = _reconciliation.Compare(pc, before);
                return Review(status, plan);
            });
        if (status.Phase != WorkspaceMigrationPhase.Uploaded) return Results.Conflict();
        var directory = Operation(id);
        status = status with { Phase = WorkspaceMigrationPhase.Preparing }; Save(State(id), status);
        try
        {
            var archive = Path.Combine(directory, "archive.zip");
            using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var index in status.ReceivedParts)
                { var part = Path.Combine(directory, index + ".part"); WorkspacePathSafety.EnsureNoLinks(part); using var input = File.OpenRead(part); input.CopyTo(output); }
                output.Flush(true);
            }
            using (var input = File.OpenRead(archive))
                if (input.Length != status.ArchiveBytes || !Convert.ToHexString(SHA256.HashData(input)).Equals(status.ArchiveSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            var stage = new WorkspaceMigrationService().Stage(archive, Path.Combine(directory, "stage"), status.ArchiveSha256);
            if (!stage.Succeeded) return Block(status, stage.Issues);
            return commands.Read(() =>
            {
                if (commands.HasPending()) return Block(status, [new("PendingCommands", "", "Une commande métier a un résultat incertain.")]);
                var before = _reconciliation.Capture(storage.WorkspaceRoot);
                var snapshot = Path.Combine(directory, "server");
                CopyTree(storage.WorkspaceRoot, snapshot);
                if (_reconciliation.Capture(snapshot).Fingerprint != before.Fingerprint || _reconciliation.Capture(storage.WorkspaceRoot).Fingerprint != before.Fingerprint) throw new InvalidDataException();
                var plan = _reconciliation.Compare(_reconciliation.Capture(Path.Combine(directory, "stage", "workspace")), before);
                return Review(status, plan);
            });
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or JsonException or ArgumentException)
        { return Block(status, [new("PreparationFailed", "", "La préparation n’a pas abouti. Conservez les sauvegardes et recommencez une nouvelle reprise.")]); }
    }
    private IResult Review(WorkspaceMigrationStatus status, WorkspaceReconciliationPlan plan)
    {
        status = status with { Plan = plan, Phase = WorkspaceMigrationPhase.Prepared, Issues = plan.Issues };
        if (plan.Issues.Any(i => i.Blocking)) return Block(status, plan.Issues);
        var conflicts = plan.Files.Where(f => f.Kind == WorkspaceDifferenceKind.Conflict).Select(f => new WorkspaceReconciliationChoice(f.Path, WorkspaceConflictChoice.Pc)).ToArray();
        if (conflicts.Length > MaxChoices || JsonSerializer.SerializeToUtf8Bytes(conflicts, CommandStore.Json).Length > ChoicesBodyBytes)
            return Block(status, [new("TooManyConflicts", "", "Le bilan dépasse la limite de choix de cette version. Aucune activation n’est possible.")]);
        return Candidate(status, []);
    }
    private IResult Block(WorkspaceMigrationStatus status, IReadOnlyList<MigrationIssue> issues)
    {
        status = status with { Phase = WorkspaceMigrationPhase.Blocked, Issues = issues, CandidateFingerprint = null, ReviewHash = null };
        Save(State(status.OperationId), status); return Results.Ok(status);
    }
    public IResult Choices(Guid id, IReadOnlyList<WorkspaceReconciliationChoice> choices)
    {
        var status = Load<WorkspaceMigrationStatus>(State(id));
        if (status.Phase != WorkspaceMigrationPhase.Prepared || status.Plan is null) return Results.Conflict();
        var conflicts = status.Plan.Files.Where(f => f.Kind == WorkspaceDifferenceKind.Conflict).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        if (choices.Count > MaxChoices || choices.Count > conflicts.Count || choices.Any(c => c is null || !Enum.IsDefined(c.Choice)
            || !conflicts.Contains(c.Path))
            || choices.Select(c => c.Path).Distinct(StringComparer.Ordinal).Count() != choices.Count) return Results.BadRequest();
        return Candidate(status, choices);
    }
    private IResult Candidate(WorkspaceMigrationStatus status, IReadOnlyList<WorkspaceReconciliationChoice> choices)
    {
        var plan = status.Plan!;
        status = status with { Choices = choices, ReviewHash = null, CandidateFingerprint = null, Issues = plan.Issues };
        var chosen = choices.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        if (plan.Files.Any(f => f.Kind == WorkspaceDifferenceKind.Conflict && !chosen.Contains(f.Path)))
        { Save(State(status.OperationId), status); return Results.Ok(status); }
        var transaction = Transaction(storage.WorkspaceRoot, status.OperationId); PrivateDirectory(transaction);
        var candidate = Path.Combine(transaction, "candidate");
        // Only this generated candidate is replaceable; retained backups are never removed.
        if (Directory.Exists(candidate)) { WorkspacePathSafety.EnsureNoLinks(candidate); CheckTree(candidate); Directory.Delete(candidate, true); }
        var result = _reconciliation.BuildCandidate(Path.Combine(Operation(status.OperationId), "stage", "workspace"), Path.Combine(Operation(status.OperationId), "server"), candidate, plan, choices);
        Save(Path.Combine(Operation(status.OperationId), "version-changes.json"), result.VersionChanges);
        status = status with { Issues = result.Issues, ReviewHash = result.Succeeded ? _reconciliation.ReviewHash(plan, choices) : null, CandidateFingerprint = result.Succeeded ? result.Snapshot.Fingerprint : null };
        Save(State(status.OperationId), status); return Results.Ok(status);
    }
    public IResult Apply(Guid id, ApplyWorkspaceMigration input) => commands.Read<IResult>(() =>
    {
        var status = Load<WorkspaceMigrationStatus>(State(id));
        if (status.Receipt is not null)
            return input.Confirmed && status.Receipt.CommandId == input.CommandId && status.ReviewHash == input.ReviewHash ? Results.Ok(status) : Results.Conflict();
        if (!input.Confirmed || input.CommandId == Guid.Empty || status.Phase != WorkspaceMigrationPhase.Prepared || status.Plan is null
            || status.CandidateFingerprint is null || status.ReviewHash is null || status.ReviewHash != input.ReviewHash) return Results.Conflict();
        if (commands.HasPending()) return RejectBeforeActivation("PendingCommands", "Une commande métier a un résultat incertain. Cette reprise n’a pas été activée.");
        var candidate = Path.Combine(Transaction(storage.WorkspaceRoot, id), "candidate");
        var backup = Path.Combine(Operation(id), "server");
        var current = _reconciliation.CaptureForReconciliation(storage.WorkspaceRoot, status.Plan);
        var candidateSnapshot = _reconciliation.CaptureForReconciliation(candidate, status.Plan);
        var backupSnapshot = _reconciliation.CaptureForReconciliation(backup, status.Plan);
        if (current.Issues.Any(i => i.Blocking) || backupSnapshot.Issues.Any(i => i.Blocking)
            || current.Fingerprint != status.Plan.ServerFingerprint || backupSnapshot.Fingerprint != current.Fingerprint
            || candidateSnapshot.Fingerprint != status.CandidateFingerprint || candidateSnapshot.Issues.Any(i => i.Blocking))
            return RejectBeforeActivation("ServerChanged", "Les données ou la copie vérifiée ont changé. Cette reprise n’a pas été activée ; préparez une nouvelle comparaison.");
        if (DriveInfo.GetDrives().Any(d => Path.TrimEndingDirectorySeparator(d.Name).Equals(storage.WorkspaceRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) return Results.Conflict();
        // Probe the parent volume before writing cutover intent.
        var probe = Path.Combine(Transaction(storage.WorkspaceRoot, id), "probe"); Directory.CreateDirectory(probe);
        Directory.Move(probe, probe + "-moved"); Directory.Delete(probe + "-moved");
        var receipt = new WorkspaceMigrationReceipt(id, input.CommandId, status.CandidateFingerprint, DateTimeOffset.UtcNow, status.Plan.PcFingerprint, status.Plan.ServerFingerprint, true);
        var journal = new SwapJournal(id, receipt, status.ReviewHash, "Intent");
        try
        {
            Save(Path.Combine(Operation(id), "swap.json"), journal);
            status = status with { Phase = WorkspaceMigrationPhase.Applying }; Save(State(id), status);
        }
        catch { commands.BlockUntilRecovery(); throw; }
        // Request cancellation must never interrupt the two directory renames or their recovery.
        Cutover(storage.WorkspaceRoot, Operation(id), journal, block: commands.BlockUntilRecovery);
        return Status(id);
        IResult RejectBeforeActivation(string code, string message)
        {
            // A durable terminal state lets a disconnected client start a new review without replaying this Apply.
            _ = Block(status, [new(code, "", message)]);
            return Results.Conflict(new { error = message });
        }
    });
    internal static void CheckTree(string source)
    {
        WorkspacePathSafety.EnsureNoLinks(source);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        { WorkspacePathSafety.EnsureNoLinks(path); if (Directory.Exists(path)) CheckTree(path); }
    }
    private static void CopyTree(string source, string target)
    {
        CheckTree(source); PrivateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        { var to = Path.Combine(target, Path.GetFileName(path)); if (Directory.Exists(path)) CopyTree(path, to); else { using var input = File.OpenRead(path); using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output); output.Flush(true); } }
    }
    public sealed record SwapJournal(Guid OperationId, WorkspaceMigrationReceipt Receipt, string ReviewHash, string Phase);
    internal static void Cutover(string workspace, string operation, SwapJournal journal, Action<string, string>? move = null,
        Action<string, object>? persist = null, Action? block = null)
    {
        persist ??= (path, value) => Save(path, value);
        try { RecoverOne(workspace, operation, journal, move, persist); }
        catch (Exception)
        {
            try
            {
                var old = Path.Combine(Transaction(workspace, journal.OperationId), "old");
                var candidate = Path.Combine(Transaction(workspace, journal.OperationId), "candidate");
                if (Directory.Exists(workspace) && Directory.Exists(old) && !Directory.Exists(candidate))
                {
                    // Installation finished: retry the durable receipt before releasing the business lock.
                    RecoverOne(workspace, operation, Load<SwapJournal>(Path.Combine(operation, "swap.json")), persist: persist);
                    return;
                }
                if (!Directory.Exists(workspace) && !File.Exists(workspace) && Directory.Exists(old))
                {
                    persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Restoring" });
                    RecoverOne(workspace, operation, journal with { Phase = "Restoring" }, persist: persist);
                }
                else block?.Invoke();
            }
            catch { block?.Invoke(); throw; }
            throw;
        }
    }
    public static void Recover(string workspace, string stateRoot)
    {
        var root = Path.Combine(stateRoot, "workspace-migrations"); WorkspacePathSafety.EnsureNoLinks(root);
        if (!Directory.Exists(root)) return;
        var pending = new List<(string Directory, SwapJournal Journal)>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            WorkspacePathSafety.EnsureNoLinks(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) throw new InvalidDataException("État de reprise invalide.");
            var path = Path.Combine(directory, "swap.json");
            if (!File.Exists(path))
            {
                var statePath = Path.Combine(directory, "state.json");
                if (File.Exists(statePath))
                {
                    var state = Load<WorkspaceMigrationStatus>(statePath);
                    if (state.OperationId != id || state.Phase == WorkspaceMigrationPhase.Applying) throw new InvalidDataException();
                    if (state.Phase == WorkspaceMigrationPhase.Preparing)
                        Save(statePath, state with { Phase = WorkspaceMigrationPhase.Blocked, ReviewHash = null, CandidateFingerprint = null,
                            Issues = [new("PreparationInterrupted", "", "La préparation a été interrompue. Une nouvelle reprise est requise.")] });
                }
                continue;
            }
            var journal = Load<SwapJournal>(path);
            if (Load<string>(Path.Combine(directory, "workspace-identity.json")) != WorkspaceIdentity(workspace)) throw new InvalidDataException("La racine configurée diffère de celle de l’activation.");
            if (journal.OperationId != id || journal.Receipt.OperationId != id || journal.Receipt.CommandId == Guid.Empty
                || !HashValid(journal.Receipt.Fingerprint) || !HashValid(journal.Receipt.PcFingerprint) || !HashValid(journal.Receipt.ServerBeforeFingerprint) || !HashValid(journal.ReviewHash)
                || !journal.Receipt.BackupVerified || journal.Receipt.AppliedAt == default
                || journal.Phase is not ("Intent" or "OldMoved" or "Installed" or "Completed" or "Restoring" or "Restored")) throw new InvalidDataException("Journal de reprise invalide.");
            if (journal.Phase is not ("Completed" or "Restored")) pending.Add((directory, journal));
        }
        if (pending.Count > 1) throw new InvalidDataException("Plusieurs activations incertaines.");
        foreach (var item in pending) RecoverOne(workspace, item.Directory, item.Journal);
    }
    private static void RecoverOne(string workspace, string operation, SwapJournal journal, Action<string, string>? move = null, Action<string, object>? persist = null)
    {
        persist ??= (path, value) => Save(path, value);
        var transaction = Transaction(workspace, journal.OperationId);
        var old = Path.Combine(transaction, "old"); var candidate = Path.Combine(transaction, "candidate");
        CheckExisting(workspace); CheckExisting(old); CheckExisting(candidate);
        var service = new WorkspaceReconciliationService();
        var statePath = Path.Combine(operation, "state.json"); var state = Load<WorkspaceMigrationStatus>(statePath);
        if (state.OperationId != journal.OperationId || state.ReviewHash != journal.ReviewHash || state.CandidateFingerprint != journal.Receipt.Fingerprint
            || state.Plan?.ServerFingerprint != journal.Receipt.ServerBeforeFingerprint || state.Plan.PcFingerprint != journal.Receipt.PcFingerprint
            || service.ReviewHash(state.Plan, state.Choices) != journal.ReviewHash
            || state.Phase is not (WorkspaceMigrationPhase.Prepared or WorkspaceMigrationPhase.Applying or WorkspaceMigrationPhase.Applied or WorkspaceMigrationPhase.Blocked)) throw new InvalidDataException();
        bool Matches(string path, string hash) => Directory.Exists(path) && service.CaptureForReconciliation(path, state.Plan) is var snapshot && snapshot.Fingerprint == hash && !snapshot.Issues.Any(i => i.Blocking);
        if (state.Phase == WorkspaceMigrationPhase.Applied && state.Receipt == journal.Receipt && Directory.Exists(workspace)
            && !Directory.Exists(candidate) && Matches(old, journal.Receipt.ServerBeforeFingerprint))
        {
            // The durable receipt is the commit point; later business edits must survive journal cleanup.
            persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Completed" }); return;
        }
        if (journal.Phase == "Restoring" || !Directory.Exists(old) && Matches(workspace, journal.Receipt.ServerBeforeFingerprint)
            && (journal.Phase == "OldMoved" || state.Phase == WorkspaceMigrationPhase.Blocked))
        {
            if (!Directory.Exists(workspace) && Matches(old, journal.Receipt.ServerBeforeFingerprint)) Directory.Move(old, workspace);
            if (Directory.Exists(old) || !Matches(workspace, journal.Receipt.ServerBeforeFingerprint)) throw new InvalidDataException();
            RestoreFinished(); return;
        }
        if (Directory.Exists(workspace) && Directory.Exists(old) && !Directory.Exists(candidate) && Matches(workspace, journal.Receipt.Fingerprint) && Matches(old, journal.Receipt.ServerBeforeFingerprint))
        { Finish(); return; }
        if (journal.Phase == "Intent" && !Directory.Exists(old) && Matches(workspace, journal.Receipt.ServerBeforeFingerprint) && Matches(candidate, journal.Receipt.Fingerprint))
        {
            persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Intent" });
            (move ?? Directory.Move)(workspace, old);
            journal = journal with { Phase = "OldMoved" }; persist(Path.Combine(operation, "swap.json"), journal);
        }
        if (!Directory.Exists(workspace) && Matches(old, journal.Receipt.ServerBeforeFingerprint))
        {
            if (Matches(candidate, journal.Receipt.Fingerprint))
            { (move ?? Directory.Move)(candidate, workspace); persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Installed" }); Finish(); return; }
            if (!Directory.Exists(candidate))
            {
                persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Restoring" });
                Directory.Move(old, workspace); RestoreFinished(); return;
            }
        }
        throw new InvalidDataException("Activation ambiguë ; vérification manuelle requise.");
        void Finish()
        {
            persist(statePath, state with { Phase = WorkspaceMigrationPhase.Applied, Receipt = journal.Receipt });
            persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Completed" });
        }
        void RestoreFinished()
        {
            // Keep the immutable anchors until the terminal journal is durable.
            persist(statePath, state with { Phase = WorkspaceMigrationPhase.Blocked,
                Issues = [new("ActivationRestored", "", "L’ancien atelier a été restauré. Une nouvelle préparation est requise.")] });
            persist(Path.Combine(operation, "swap.json"), journal with { Phase = "Restored" });
        }
        static void CheckExisting(string path) { WorkspacePathSafety.EnsureNoLinks(path); if (Directory.Exists(path)) CheckTree(path); }
    }
}

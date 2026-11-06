using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Server.Migration;
using MS3DPRINT.Manager.Server.Workspace;
using Microsoft.AspNetCore.Http;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WorkspaceMigrationRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Installed_candidate_requires_durable_receipt_before_business_access(bool persistentFailure)
    {
        using var fixture = new RecoveryFixture(); var storage = fixture.EmptyStorage(); fixture.Seed("Intent", 0);
        var commands = new CommandStore(storage); var attempts = 0;
        var operation = Path.GetDirectoryName(fixture.StatePath)!;
        var journal = WorkspaceMigrationStore.Load<WorkspaceMigrationStore.SwapJournal>(Path.Combine(operation, "swap.json"));
        void Persist(string path, object value)
        {
            if (value is WorkspaceMigrationStatus { Phase: WorkspaceMigrationPhase.Applied } && (++attempts == 1 || persistentFailure)) throw new IOException("Simulated receipt failure");
            WorkspaceMigrationStore.Save(path, value);
        }
        if (persistentFailure)
        {
            Assert.Throws<IOException>(() => commands.Read(() => { WorkspaceMigrationStore.Cutover(fixture.Workspace, operation, journal, persist: Persist, block: commands.BlockUntilRecovery); return true; }));
            Assert.Throws<WorkspaceUnavailableException>(() => commands.Read(() => true));
            var invoked = false;
            var response = commands.Execute(Guid.NewGuid(), new DefaultHttpContext().Request, new { }, () => { invoked = true; return new CommandResult(200, new { }); });
            Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode); Assert.False(invoked);
            Assert.IsAssignableFrom<IResult>(new WorkspaceMigrationStore(storage, commands).Status(fixture.Id));
            Assert.Null(WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Receipt);
            _ = new ServerStorage(fixture.Configuration);
        }
        else commands.Read(() => { WorkspaceMigrationStore.Cutover(fixture.Workspace, operation, journal, persist: Persist, block: commands.BlockUntilRecovery); return true; });
        Assert.Equal("new", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
        Assert.Equal(WorkspaceMigrationPhase.Applied, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Phase);
        if (!persistentFailure) Assert.True(commands.Read(() => true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Startup_recovers_each_restore_rename_and_state_write_boundary(int boundary)
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("Restoring", 1);
        Directory.Delete(Path.Combine(fixture.Transaction, "candidate"), true);
        if (boundary >= 1) Directory.Move(Path.Combine(fixture.Transaction, "old"), fixture.Workspace);
        if (boundary >= 2)
        {
            var state = WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath);
            WorkspaceMigrationStore.Save(fixture.StatePath, state with { Phase = WorkspaceMigrationPhase.Blocked });
        }
        _ = new ServerStorage(fixture.Configuration); _ = new ServerStorage(fixture.Configuration);
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
        Assert.Equal(WorkspaceMigrationPhase.Blocked, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Phase);
    }

    [Fact]
    public void Durable_receipt_allows_restart_to_finish_journal_after_later_business_writes()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("Installed", 2);
        var operation = Path.GetDirectoryName(fixture.StatePath)!; var journal = WorkspaceMigrationStore.Load<WorkspaceMigrationStore.SwapJournal>(Path.Combine(operation, "swap.json"));
        var state = WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath);
        WorkspaceMigrationStore.Save(fixture.StatePath, state with { Phase = WorkspaceMigrationPhase.Applied, Receipt = journal.Receipt });
        File.WriteAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt"), "later-business-write");
        _ = new ServerStorage(fixture.Configuration);
        Assert.Equal("later-business-write", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
        Assert.Equal(journal.Receipt, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Receipt);
    }
    [Theory]
    [InlineData("Intent", 0)]
    [InlineData("Intent", 1)]
    [InlineData("OldMoved", 1)]
    [InlineData("OldMoved", 2)]
    [InlineData("Installed", 2)]
    public void Startup_finishes_verified_swap_at_every_rename_boundary(string phase, int moves)
    {
        using var fixture = new RecoveryFixture(); fixture.Seed(phase, moves);
        var storage = new ServerStorage(fixture.Configuration);
        Assert.Equal("new", File.ReadAllText(Path.Combine(storage.WorkspaceRoot, "02_MODELES_3D", "test.txt")));
        var status = WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath);
        Assert.Equal(WorkspaceMigrationPhase.Applied, status.Phase);
        Assert.Equal(fixture.CommandId, status.Receipt!.CommandId);
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Transaction, "old", "02_MODELES_3D", "test.txt")));
        _ = new ServerStorage(fixture.Configuration);
        Assert.Equal(status.Receipt, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Receipt);
    }

    [Fact]
    public void Startup_rejects_reuse_of_a_journal_with_another_configured_root()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("Intent", 0);
        WorkspaceMigrationStore.Save(Path.Combine(Path.GetDirectoryName(fixture.StatePath)!, "workspace-identity.json"), new string('0',64));
        Assert.Throws<InvalidDataException>(() => new ServerStorage(fixture.Configuration));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
    }

    [Fact]
    public void Startup_blocks_interrupted_preparation_without_touching_active_workspace()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("Intent", 0);
        File.Delete(Path.Combine(Path.GetDirectoryName(fixture.StatePath)!, "swap.json"));
        var status = WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath);
        WorkspaceMigrationStore.Save(fixture.StatePath, status with { Phase = WorkspaceMigrationPhase.Preparing });
        _ = new ServerStorage(fixture.Configuration);
        Assert.Equal(WorkspaceMigrationPhase.Blocked, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Phase);
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
    }

    [Fact]
    public void Live_cutover_failure_restores_old_root_immediately()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("Intent", 0);
        var journal = WorkspaceMigrationStore.Load<WorkspaceMigrationStore.SwapJournal>(Path.Combine(Path.GetDirectoryName(fixture.StatePath)!, "swap.json"));
        var moves = 0;
        Assert.Throws<IOException>(() => WorkspaceMigrationStore.Cutover(fixture.Workspace, Path.GetDirectoryName(fixture.StatePath)!, journal, (source, destination) =>
        { if (++moves == 2) throw new IOException("Simulated rename failure"); Directory.Move(source, destination); }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
        Assert.Equal(WorkspaceMigrationPhase.Blocked, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Phase);
    }

    [Fact]
    public void Startup_restores_old_root_when_candidate_is_missing()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("OldMoved", 1);
        Directory.Delete(Path.Combine(fixture.Transaction, "candidate"), true);
        _ = new ServerStorage(fixture.Configuration);
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Workspace, "02_MODELES_3D", "test.txt")));
        Assert.Equal(WorkspaceMigrationPhase.Blocked, WorkspaceMigrationStore.Load<WorkspaceMigrationStatus>(fixture.StatePath).Phase);
    }

    [Fact]
    public void Startup_refuses_ambiguous_or_tampered_candidate_without_discarding_old()
    {
        using var fixture = new RecoveryFixture(); fixture.Seed("OldMoved", 1);
        File.WriteAllText(Path.Combine(fixture.Transaction, "candidate", "02_MODELES_3D", "test.txt"), "tampered");
        Assert.Throws<InvalidDataException>(() => new ServerStorage(fixture.Configuration));
        Assert.False(Directory.Exists(fixture.Workspace));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Transaction, "old", "02_MODELES_3D", "test.txt")));
    }

    [Fact]
    public async Task Apply_requires_confirmation_and_exact_review_then_is_durably_idempotent()
    {
        using var host = new ServerFixture(); using var client = await host.AuthenticatedClient();
        Directory.CreateDirectory(Path.Combine(host.Workspace, "02_MODELES_3D"));
        File.WriteAllText(Path.Combine(host.Workspace, "02_MODELES_3D", "server.txt"), "server");
        var pc = Path.Combine(host.Root, "pc"); Directory.CreateDirectory(Path.Combine(pc, "02_MODELES_3D"));
        File.WriteAllText(Path.Combine(pc, "02_MODELES_3D", "pc.txt"), "pc");
        var archive = Path.Combine(host.Root, "pc.zip"); new WorkspaceBackupService().Create(pc, archive, true);
        var bytes = File.ReadAllBytes(archive); var hash = Convert.ToHexString(SHA256.HashData(bytes)); var id = Guid.NewGuid();
        (await client.PostAsJsonAsync(WorkspaceMigrationApiTests.Url, new StartWorkspaceMigration(id, bytes.Length, hash))).EnsureSuccessStatusCode();
        using var part = new HttpRequestMessage(HttpMethod.Put, WorkspaceMigrationApiTests.Url + $"/{id}/parts/0") { Content = new ByteArrayContent(bytes) }; part.Headers.Add("X-MS3DFORGE-SHA256", hash);
        (await client.SendAsync(part)).EnsureSuccessStatusCode();
        var prepared = await (await client.PostAsync(WorkspaceMigrationApiTests.Url + $"/{id}/prepare", null)).Content.ReadFromJsonAsync<WorkspaceMigrationStatus>(WorkspaceMigrationApiTests.Json);
        Assert.Equal(WorkspaceMigrationPhase.Prepared, prepared!.Phase); Assert.NotNull(prepared.ReviewHash);
        var command = new ApplyWorkspaceMigration(Guid.NewGuid(), prepared.ReviewHash, true); var url = WorkspaceMigrationApiTests.Url + $"/{id}/apply";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, command with { Confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, command with { ReviewHash = new string('0', 64) })).StatusCode);
        var server = Path.Combine(host.Workspace, "02_MODELES_3D", "server.txt");
        var applied = await (await client.PostAsJsonAsync(url, command)).Content.ReadFromJsonAsync<WorkspaceMigrationStatus>(WorkspaceMigrationApiTests.Json);
        Assert.Equal(WorkspaceMigrationPhase.Applied, applied!.Phase); Assert.True(applied.Receipt!.BackupVerified);
        Assert.Equal("server", File.ReadAllText(server)); Assert.Equal("pc", File.ReadAllText(Path.Combine(host.Workspace, "02_MODELES_3D", "pc.txt")));
        var repeated = await (await client.PostAsJsonAsync(url, command)).Content.ReadFromJsonAsync<WorkspaceMigrationStatus>(WorkspaceMigrationApiTests.Json);
        Assert.Equal(applied.Receipt, repeated!.Receipt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, command with { CommandId = Guid.NewGuid() })).StatusCode);
    }

    private sealed class RecoveryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "migration-recovery-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid(); public Guid CommandId { get; } = Guid.NewGuid();
        public string Workspace => Path.Combine(_root, "workspace");
        private string StateRoot => Path.Combine(_root, "state");
        private string Operation => Path.Combine(StateRoot, "workspace-migrations", Id.ToString("N"));
        public string StatePath => Path.Combine(Operation, "state.json");
        public string Transaction => WorkspaceMigrationStore.Transaction(Workspace, Id);
        public IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Server:WorkspaceRoot"] = Workspace, ["Server:StateRoot"] = StateRoot }).Build();
        public ServerStorage EmptyStorage() { Directory.CreateDirectory(Workspace); return new ServerStorage(Configuration); }
        public void Seed(string phase, int moves)
        {
            Directory.CreateDirectory(Path.Combine(Workspace, "02_MODELES_3D")); Directory.CreateDirectory(Operation); Directory.CreateDirectory(Path.Combine(Transaction, "candidate", "02_MODELES_3D"));
            File.WriteAllText(Path.Combine(Workspace, "02_MODELES_3D", "test.txt"), "old");
            File.WriteAllText(Path.Combine(Transaction, "candidate", "02_MODELES_3D", "test.txt"), "new");
            var service = new WorkspaceReconciliationService(); var before = service.Capture(Workspace); var after = service.Capture(Path.Combine(Transaction, "candidate"));
            var plan = service.Compare(after, before); var choices = new[] { new WorkspaceReconciliationChoice("02_MODELES_3D/test.txt", WorkspaceConflictChoice.Pc) }; var review = service.ReviewHash(plan, choices);
            var receipt = new WorkspaceMigrationReceipt(Id, CommandId, after.Fingerprint, DateTimeOffset.UtcNow, after.Fingerprint, before.Fingerprint, true);
            WorkspaceMigrationStore.Save(StatePath, new WorkspaceMigrationStatus(Id, WorkspaceMigrationPhase.Applying, 1, new string('A',64), 524288, [0], plan, choices, review, after.Fingerprint, null, []));
            WorkspaceMigrationStore.Save(Path.Combine(Operation, "workspace-identity.json"), WorkspaceMigrationStore.WorkspaceIdentity(Workspace));
            WorkspaceMigrationStore.Save(Path.Combine(Operation, "swap.json"), new WorkspaceMigrationStore.SwapJournal(Id, receipt, review, phase));
            if (moves >= 1) Directory.Move(Workspace, Path.Combine(Transaction, "old"));
            if (moves >= 2) Directory.Move(Path.Combine(Transaction, "candidate"), Workspace);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}

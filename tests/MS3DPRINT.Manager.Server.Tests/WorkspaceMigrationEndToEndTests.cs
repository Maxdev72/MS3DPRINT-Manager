using System.Net.Http.Json;
using System.Security.Cryptography;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Server.Workspace;
using System.Text.Json;

namespace MS3DPRINT.Manager.Server.Tests;

/// <summary>Exercises the native transport against the real authenticated server pipeline.</summary>
public sealed class WorkspaceMigrationEndToEndTests
{
    [Theory]
    [InlineData("fresh")]
    [InlineData("old")]
    [InlineData("drift")]
    [InlineData("swap")]
    [InlineData("other")]
    public async Task Server_historical_project_folder_keeps_its_documents_without_inventing_a_profile_and_resumes_old_upload(string scenario)
    {
        using var host = new ServerFixture(); host.CreateOwner();
        var local = Directory.CreateTempSubdirectory("historical-folder-e2e-").FullName;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var paths = new WorkspaceMetadataPaths(host.Workspace);
            var owner = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "CLIENT_TEST", "CT", "Client test", null, null, null, null, new(null, null, null, null, null), now, now, RelativePath: "01_CLIENTS/CLIENT_TEST");
            new ClientProfileStore(paths).Create(owner);
            var relative = "01_CLIENTS/CLIENT_TEST/CT-2026-001_HISTORICAL";
            Directory.CreateDirectory(Path.Combine(host.Workspace, relative));
            File.WriteAllText(Path.Combine(host.Workspace, relative, "keep.txt"), "historical document");
            var pc = Directory.CreateDirectory(Path.Combine(local, "pc")).FullName; Write(pc, "pc.txt", "latest PC");
            var service = new WorkspaceReconciliationService(); var before = service.Capture(host.Workspace).Fingerprint;
            Assert.Contains(service.Capture(host.Workspace).Issues, i => i.Code == "ProjectProfileMissing" && !i.Blocking);
            using var handler = new LostApplyResponseHandler(host.Server.CreateHandler(), false);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            var client = new RemoteWorkspaceClient(http, http.BaseAddress); await client.LoginAsync("owner", host.Password);
            using var migration = GuidedWorkspaceMigration.Create(http.BaseAddress, "owner", pc, Path.Combine(local, "private"));
            Assert.True(migration.Prepare(new HashSet<Guid>()).Succeeded);
            var status = await migration.TransferAsync(client, null, CancellationToken.None);
            Assert.Equal(WorkspaceMigrationPhase.Prepared, status.Phase);
            Assert.Contains(status.Issues, i => i.Code == "PreservedProjectFolder" && !i.Blocking && i.Path == relative);
            if (scenario != "fresh")
            {
                var old = status with { Phase = WorkspaceMigrationPhase.Blocked, CandidateFingerprint = null, ReviewHash = null,
                    Issues = [new("ProjectProfileMissing", relative, "Old validation blocks this historical folder.")] };
                var state = Path.Combine(host.State, "workspace-migrations", status.OperationId.ToString("N"), "state.json");
                File.WriteAllBytes(state, JsonSerializer.SerializeToUtf8Bytes(old, CommandStore.Json));
                if (scenario == "drift") Write(host.Workspace, "late.txt", "later server change");
                if (scenario == "swap") File.WriteAllText(Path.Combine(Path.GetDirectoryName(state)!, "swap.json"), "{}");
                if (scenario == "other") File.WriteAllBytes(state, JsonSerializer.SerializeToUtf8Bytes(old with { Issues = [.. old.Issues, new("MissingClient", "", "Client missing.")] }, CommandStore.Json));
                var parts = handler.PartCalls;
                if (scenario == "swap")
                {
                    await Assert.ThrowsAsync<RemoteWorkspaceException>(() => migration.TransferAsync(client, null, CancellationToken.None));
                    Assert.Equal(parts, handler.PartCalls); Assert.Equal(0, handler.ApplyCalls); Assert.True(migration.SourceStable()); return;
                }
                status = await migration.TransferAsync(client, null, CancellationToken.None);
                if (scenario is "drift" or "other")
                {
                    Assert.Equal(WorkspaceMigrationPhase.Blocked, status.Phase); Assert.Equal(parts, handler.PartCalls);
                    Assert.Equal(0, handler.ApplyCalls); Assert.True(migration.SourceStable()); return;
                }
                Assert.Equal(WorkspaceMigrationPhase.Prepared, status.Phase); Assert.Equal(parts, handler.PartCalls);
            }
            Assert.Equal(before, service.Capture(host.Workspace).Fingerprint); Assert.Equal(0, handler.ApplyCalls);
            Assert.Empty(new ProjectProfileStore(paths).LoadAll());
            status = await migration.ApplyAsync(client, status, CancellationToken.None);
            Assert.Equal(WorkspaceMigrationPhase.Applied, status.Phase); Assert.Equal(1, handler.ApplyCalls);
            Assert.Equal("historical document", File.ReadAllText(Path.Combine(host.Workspace, relative, "keep.txt")));
            Assert.Equal("latest PC", Read(host.Workspace, "pc.txt")); Assert.True(migration.SourceStable());
            Assert.Empty(new ProjectProfileStore(paths).LoadAll());
            Assert.Contains(service.Capture(host.Workspace).Issues, i => i.Code == "ProjectProfileMissing" && !i.Blocking);
        }
        finally { Directory.Delete(local, true); }
    }
    [Fact]
    public async Task Pc_project_folder_without_profile_is_transferred_as_documents()
    {
        using var host = new ServerFixture(); host.CreateOwner();
        var local = Directory.CreateTempSubdirectory("pc-historical-e2e-").FullName;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var pc = Directory.CreateDirectory(Path.Combine(local, "pc")).FullName;
            new ClientProfileStore(new WorkspaceMetadataPaths(pc)).Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "CLIENT_TEST", "CT",
                "Client test", null, null, null, null, new(null, null, null, null, null), now, now, RelativePath: "01_CLIENTS/CLIENT_TEST"));
            var relative = "01_CLIENTS/CLIENT_TEST/CT-2026-001_NO_PROFILE";
            Directory.CreateDirectory(Path.Combine(pc, relative)); File.WriteAllText(Path.Combine(pc, relative, "model.txt"), "pc document");
            using var http = new HttpClient(host.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
            var client = new RemoteWorkspaceClient(http, http.BaseAddress); await client.LoginAsync("owner", host.Password);
            using var migration = GuidedWorkspaceMigration.Create(http.BaseAddress, "owner", pc, Path.Combine(local, "private"));
            Assert.True(migration.Prepare(new HashSet<Guid>()).Succeeded);
            var status = await migration.TransferAsync(client, null, CancellationToken.None);
            Assert.Equal(WorkspaceMigrationPhase.Prepared, status.Phase);
            Assert.Contains(status.Issues, i => i.Code == "PreservedProjectFolder" && !i.Blocking && i.Path == relative);
            status = await migration.ApplyAsync(client, status, CancellationToken.None);
            Assert.Equal(WorkspaceMigrationPhase.Applied, status.Phase);
            Assert.Equal("pc document", File.ReadAllText(Path.Combine(host.Workspace, relative, "model.txt")));
            Assert.Empty(new ProjectProfileStore(new WorkspaceMetadataPaths(host.Workspace)).LoadAll());
        }
        finally { Directory.Delete(local, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_transfer_preserves_both_workshops_and_recovers_a_lost_activation_response(bool loseResponse)
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        var local = Directory.CreateTempSubdirectory("migration-native-e2e-").FullName;
        try
        {
            var pc = Directory.CreateDirectory(Path.Combine(local, "pc")).FullName;
            Write(pc, "shared.txt", "Latest PC content");
            Write(host.Workspace, "shared.txt", "Older server content");
            Write(host.Workspace, "server-only.txt", "Keep this server document");
            var random = new byte[800000]; RandomNumberGenerator.Fill(random);
            File.WriteAllBytes(Path.Combine(pc, "02_MODELES_3D", "large.bin"), random);
            var compare = new WorkspaceReconciliationService();
            var sourceBefore = compare.Capture(pc).Fingerprint;
            var serverBefore = compare.Capture(host.Workspace).Fingerprint;
            var archive = Path.Combine(local, "source.zip");
            var backup = new WorkspaceBackupService().Create(pc, archive, true);
            using var handler = new LostApplyResponseHandler(host.Server.CreateHandler(), loseResponse);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            var client = new RemoteWorkspaceClient(http, http.BaseAddress);
            await client.LoginAsync("owner", host.Password);
            var session = client.CaptureSession("owner");
            var capabilities = await client.GetMigrationCapabilitiesAsync();
            var start = new StartWorkspaceMigration(Guid.NewGuid(), new FileInfo(archive).Length, backup.ArchiveSha256);
            var status = await client.StartMigrationAsync(start);
            Assert.Equal(start.CommandId, status.OperationId);
            Assert.True(start.Bytes > capabilities.ChunkBytes);
            var progress = new BytesProgress();
            status = await client.UploadMigrationAsync(archive, status, progress);
            Assert.Equal(WorkspaceMigrationPhase.Uploaded, status.Phase);
            Assert.Equal(start.Bytes, progress.Bytes);
            Assert.True(status.ReceivedParts.Length > 1);
            status = await client.PrepareMigrationAsync(status.OperationId);
            Assert.NotNull(status.Plan);
            Assert.Contains(status.Plan.Files, f => f.Path == "02_MODELES_3D/server-only.txt" && f.Kind == WorkspaceDifferenceKind.ServerOnly);
            Assert.Equal(serverBefore, compare.Capture(host.Workspace).Fingerprint);
            status = await client.SetMigrationChoicesAsync(status.OperationId,
                status.Plan.Files.Where(f => f.Kind == WorkspaceDifferenceKind.Conflict)
                    .Select(f => new WorkspaceReconciliationChoice(f.Path, WorkspaceConflictChoice.Pc)).ToArray());
            Assert.NotNull(status.ReviewHash);
            var command = new ApplyWorkspaceMigration(Guid.NewGuid(), status.ReviewHash, true);
            if (loseResponse)
            {
                var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.ApplyMigrationAsync(status.OperationId, command));
                Assert.Equal(RemoteFailure.Uncertain, error.Kind);
                // Simulate application restart: reuse only the protected device session, then GET the durable receipt.
                client = new RemoteWorkspaceClient(http, http.BaseAddress);
                client.RestoreSession(session);
                status = await client.GetMigrationStatusAsync(status.OperationId);
            }
            else status = await client.ApplyMigrationAsync(status.OperationId, command);
            Assert.Equal(1, handler.ApplyCalls);
            Assert.Equal(WorkspaceMigrationPhase.Applied, status.Phase);
            Assert.NotNull(status.Receipt);
            Assert.True(status.Receipt.BackupVerified);
            Assert.Equal(command.CommandId, status.Receipt.CommandId);
            Assert.Equal(compare.Capture(host.Workspace).Fingerprint, status.Receipt.Fingerprint);
            Assert.Equal(sourceBefore, compare.Capture(pc).Fingerprint);
            Assert.Equal("Latest PC content", Read(host.Workspace, "shared.txt"));
            Assert.Equal("Keep this server document", Read(host.Workspace, "server-only.txt"));
            Assert.Equal(random, File.ReadAllBytes(Path.Combine(host.Workspace, "02_MODELES_3D", "large.bin")));
            Assert.Empty(await client.GetClientsAsync()); // Existing identity/session survived the workspace swap.
        }
        finally { Directory.Delete(local, true); }
    }

    [Fact]
    public async Task Guided_restart_recovers_only_the_receipt_without_resending_activation()
    {
        using var host = new ServerFixture(); host.CreateOwner();
        var local = Directory.CreateTempSubdirectory("guided-restart-e2e-").FullName;
        try
        {
            var pc = Directory.CreateDirectory(Path.Combine(local, "pc")).FullName;
            Write(pc, "pc.txt", "Source must remain intact");
            Write(host.Workspace, "server.txt", "Server-only document survives");
            using var handler = new LostApplyResponseHandler(host.Server.CreateHandler(), true);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            var client = new RemoteWorkspaceClient(http, http.BaseAddress);
            await client.LoginAsync("owner", host.Password);
            var protectedSession = client.CaptureSession("owner");
            string journalPath;
            using (var migration = GuidedWorkspaceMigration.Create(http.BaseAddress, "owner", pc, Path.Combine(local, "private")))
            {
                Assert.True(migration.Prepare(new HashSet<Guid>()).Succeeded);
                var reviewed = await migration.TransferAsync(client, null, CancellationToken.None);
                Assert.NotNull(reviewed.ReviewHash);
                var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => migration.ApplyAsync(client, reviewed, CancellationToken.None));
                Assert.Equal(RemoteFailure.Uncertain, error.Kind);
                Assert.True(migration.Journal.ApplyRequested);
                journalPath = migration.JournalPath;
                Assert.DoesNotContain(protectedSession.AccessToken, File.ReadAllText(journalPath));
                Assert.True(File.Exists(Path.Combine(migration.Journal.Directory, "original.zip")));
            }
            using var resumed = GuidedWorkspaceMigration.Load(journalPath, http.BaseAddress, "owner", pc);
            var afterRestart = new RemoteWorkspaceClient(http, http.BaseAddress); afterRestart.RestoreSession(protectedSession);
            var status = await resumed.CheckAsync(afterRestart, CancellationToken.None);
            Assert.Equal(WorkspaceMigrationPhase.Applied, status.Phase);
            Assert.NotNull(resumed.Journal.Receipt);
            // Calling the orchestration entry point after restart must still issue GET only.
            await resumed.ApplyAsync(afterRestart, status, CancellationToken.None);
            Assert.Equal(1, handler.ApplyCalls);
            Assert.True(resumed.SourceStable());
            Assert.Equal("Source must remain intact", Read(pc, "pc.txt"));
            Assert.Equal("Source must remain intact", Read(host.Workspace, "pc.txt"));
            Assert.Equal("Server-only document survives", Read(host.Workspace, "server.txt"));
        }
        finally { Directory.Delete(local, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_activation_is_terminal_and_allows_a_new_review_without_replaying_the_old_command(bool pendingCommand)
    {
        using var host = new ServerFixture(); host.CreateOwner();
        var local = Directory.CreateTempSubdirectory("migration-rejected-e2e-").FullName;
        try
        {
            var pc = Directory.CreateDirectory(Path.Combine(local, "pc")).FullName;
            Write(pc, "pc.txt", "pc"); Write(host.Workspace, "server.txt", "server");
            var archive = Path.Combine(local, "source.zip"); var backup = new WorkspaceBackupService().Create(pc, archive, true);
            using var http = host.Browser(); var client = new RemoteWorkspaceClient(http, http.BaseAddress!);
            await client.LoginAsync("owner", host.Password);
            async Task<WorkspaceMigrationStatus> Prepare()
            {
                var status = await client.StartMigrationAsync(new(Guid.NewGuid(), new FileInfo(archive).Length, backup.ArchiveSha256));
                status = await client.UploadMigrationAsync(archive, status);
                return await client.PrepareMigrationAsync(status.OperationId);
            }
            var reviewed = await Prepare();
            var command = new ApplyWorkspaceMigration(Guid.NewGuid(), reviewed.ReviewHash!, true);
            var pendingId = Guid.NewGuid().ToString();
            void Pending(bool insert)
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(host.State, "commands.sqlite"), Pooling = false }.ToString());
                connection.Open(); using var query = connection.CreateCommand();
                query.CommandText = insert ? "INSERT INTO commands(command_id,request_hash,state) VALUES($id,'test','Pending')" : "DELETE FROM commands WHERE command_id=$id";
                query.Parameters.AddWithValue("$id", pendingId); query.ExecuteNonQuery();
            }
            if (pendingCommand) Pending(true); else Write(host.Workspace, "server.txt", "server changed after review");
            await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.ApplyMigrationAsync(reviewed.OperationId, command));
            var rejected = await client.GetMigrationStatusAsync(reviewed.OperationId);
            Assert.Equal(WorkspaceMigrationPhase.Blocked, rejected.Phase);
            Assert.Null(rejected.Receipt);
            Assert.False(File.Exists(Path.Combine(host.Workspace, "02_MODELES_3D", "pc.txt")));
            if (pendingCommand) Pending(false); else Write(host.Workspace, "server.txt", "server");
            // Even after the cause disappears, the old command must remain terminal, never silently become valid again.
            await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.ApplyMigrationAsync(reviewed.OperationId, command));
            var fresh = await Prepare();
            fresh = await client.ApplyMigrationAsync(fresh.OperationId, new(Guid.NewGuid(), fresh.ReviewHash!, true));
            Assert.Equal(WorkspaceMigrationPhase.Applied, fresh.Phase);
            Assert.Equal("server", Read(host.Workspace, "server.txt")); Assert.Equal("pc", Read(host.Workspace, "pc.txt"));
        }
        finally { Directory.Delete(local, true); }
    }
    private static void Write(string root, string name, string text)
    {
        Directory.CreateDirectory(Path.Combine(root, "02_MODELES_3D"));
        File.WriteAllText(Path.Combine(root, "02_MODELES_3D", name), text);
    }
    private static string Read(string root, string name) => File.ReadAllText(Path.Combine(root, "02_MODELES_3D", name));
    private sealed class BytesProgress : IProgress<long>
    {
        public long Bytes { get; private set; }
        public void Report(long value) => Bytes = value;
    }
    private sealed class LostApplyResponseHandler(HttpMessageHandler inner, bool loseResponse) : DelegatingHandler(inner)
    {
        public int ApplyCalls { get; private set; }
        public int PartCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var apply = request.RequestUri!.AbsolutePath.EndsWith("/apply", StringComparison.Ordinal);
            if (apply) ApplyCalls++;
            if (request.RequestUri.AbsolutePath.Contains("/parts/")) PartCalls++;
            var response = await base.SendAsync(request, cancellationToken);
            if (apply && loseResponse && response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException("Synthetic lost response after server activation.");
            }
            return response;
        }
    }
}

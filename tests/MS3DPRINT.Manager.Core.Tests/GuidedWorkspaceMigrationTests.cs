using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
namespace MS3DPRINT.Manager.Core.Tests;
public sealed class GuidedWorkspaceMigrationTests
{
    [Fact]
    public void Original_backup_and_portable_copy_preserve_PC_bytes_and_hold_exclusive_resume_lease()
    {
        using var files = new Files(); var server = new Uri("https://example.test");
        using var migration = GuidedWorkspaceMigration.Create(server, "owner", files.Source, files.Private);
        var original = File.ReadAllBytes(files.Document);
        Assert.Throws<IOException>(() => GuidedWorkspaceMigration.Load(migration.JournalPath, server, "owner", files.Source));
        Assert.Throws<GuidedWorkspaceMigrationIdentityException>(() => GuidedWorkspaceMigration.Load(migration.JournalPath, server, "other", files.Source));
        var result = migration.Prepare(new HashSet<Guid>());
        Assert.True(result.Succeeded, string.Join(";", result.Issues.Select(i => i.Message)));
        Assert.Equal(original, File.ReadAllBytes(files.Document));
        Assert.True(File.Exists(Path.Combine(migration.Journal.Directory, "original.zip")));
        Assert.True(File.Exists(migration.ArchivePath));
        Assert.DoesNotContain("AccessToken", File.ReadAllText(migration.JournalPath));
        migration.Dispose();
        using var resumed = GuidedWorkspaceMigration.Load(migration.JournalPath, server, "owner", files.Source);
        Assert.Equal(migration.Journal.CommandId, resumed.Journal.CommandId);
    }
    [Fact]
    public void Changed_PC_blocks_preparation_without_rewriting_source()
    {
        using var files = new Files(); using var migration = GuidedWorkspaceMigration.Create(new Uri("https://example.test"), "owner", files.Source, files.Private);
        File.WriteAllText(files.Document, "new bytes");
        Assert.Throws<InvalidDataException>(() => migration.Prepare(new HashSet<Guid>()));
        Assert.Equal("new bytes", File.ReadAllText(files.Document));
        Assert.False(File.Exists(Path.Combine(migration.Journal.Directory, "original.zip")));
    }
    [Fact]
    public async Task Changed_upload_response_identity_blocks_next_part()
    {
        using var files = new Files(); var bytes = RandomNumberGenerator.GetBytes(524288 * 2 + 3); var archive = Path.Combine(files.Private, "archive.zip"); File.WriteAllBytes(archive, bytes);
        var id = Guid.NewGuid(); var state = Status(id, bytes); var puts = 0;
        var client = await Client(request => { puts++; return Task.FromResult(Response(state with { OperationId = Guid.NewGuid(), ReceivedParts = [0] })); });
        await Assert.ThrowsAsync<InvalidDataException>(() => client.UploadMigrationAsync(archive, state));
        Assert.Equal(1, puts);
    }
    [Fact]
    public async Task Resumed_upload_sends_only_missing_part_with_hash_and_real_confirmed_bytes()
    {
        using var files = new Files(); var bytes = RandomNumberGenerator.GetBytes(524288 + 17); var archive = Path.Combine(files.Private, "archive.zip"); File.WriteAllBytes(archive, bytes);
        var id = Guid.NewGuid(); var state = Status(id, bytes) with { ReceivedParts = [0] }; var progress = new List<long>();
        var client = await Client(async request =>
        {
            Assert.EndsWith("/parts/1", request.RequestUri!.AbsolutePath);
            Assert.Equal("native", request.Headers.GetValues("X-MS3DFORGE-Client").Single());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var part = await request.Content!.ReadAsByteArrayAsync(); Assert.Equal(bytes[524288..], part);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(part)), request.Headers.GetValues("X-MS3DFORGE-SHA256").Single());
            return Response(state with { ReceivedParts = [0, 1], Phase = WorkspaceMigrationPhase.Uploaded });
        });
        await client.UploadMigrationAsync(archive, state, new Progress(progress));
        Assert.Equal(new long[] { 524288, 524305 }, progress);
    }
    [Fact]
    public async Task Lost_apply_response_persists_intent_then_reopened_operation_only_GETs_status()
    {
        using var files = new Files(); var server = new Uri("https://example.test");
        var migration = GuidedWorkspaceMigration.Create(server, "owner", files.Source, files.Private);
        migration.Prepare(new HashSet<Guid>());
        WorkspaceMigrationStatus? remote = null; var applyCalls = 0; var gets = 0;
        var client = await Client(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("capabilities")) return Response(new WorkspaceMigrationCapabilities(1, 524288, 4294967296));
            if (path.EndsWith("/operations")) { var command = await request.Content!.ReadFromJsonAsync<StartWorkspaceMigration>(); remote = new(command!.CommandId, WorkspaceMigrationPhase.Uploaded, command.Bytes, command.Sha256, 524288, [0], null, [], null, null, null, []); return Response(remote); }
            if (path.Contains("/parts/")) return Response(remote!);
            if (path.EndsWith("/prepare"))
            {
                var reconciliation = new WorkspaceReconciliationService(); var pc = reconciliation.Capture(files.Source); var plan = reconciliation.Compare(pc, pc);
                remote = remote! with { Phase = WorkspaceMigrationPhase.Prepared, Plan = plan, ReviewHash = reconciliation.ReviewHash(plan, []), CandidateFingerprint = pc.Fingerprint }; return Response(remote);
            }
            if (path.EndsWith("/apply"))
            {
                applyCalls++; Assert.True(GuidedJournalHasIntent(migration.JournalPath)); throw new HttpRequestException("response lost");
            }
            Assert.Equal(HttpMethod.Get, request.Method); gets++; return Response(remote!);
        });
        var reviewed = await migration.TransferAsync(client, null, default);
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => migration.ApplyAsync(client, reviewed, default)); Assert.Equal(RemoteFailure.Uncertain, error.Kind);
        var path = migration.JournalPath; migration.Dispose();
        using var resumed = GuidedWorkspaceMigration.Load(path, server, "owner", files.Source);
        await resumed.ApplyAsync(client, reviewed, default);
        Assert.Equal(1, applyCalls); Assert.Equal(1, gets); Assert.True(resumed.Journal.ApplyRequested);
    }
    [Fact]
    public void Missing_required_journal_fields_fail_closed_instead_of_starting_another_operation()
    {
        using var files = new Files(); var server = new Uri("https://example.test");
        var migration = GuidedWorkspaceMigration.Create(server, "owner", files.Source, files.Private);
        var path = migration.JournalPath; migration.Dispose(); File.WriteAllText(path, "{}");
        Assert.Throws<InvalidDataException>(() => GuidedWorkspaceMigration.Load(path, server, "owner", files.Source));
    }
    [Fact]
    public void New_excluded_root_requires_a_new_source_scope_acknowledgement()
    {
        using var files = new Files(); using var migration = GuidedWorkspaceMigration.Create(new Uri("https://example.test"), "owner", files.Source, files.Private);
        Directory.CreateDirectory(Path.Combine(files.Source, "unmanaged"));
        Assert.False(migration.SourceStable());
        Assert.Throws<InvalidDataException>(() => migration.RequireStableSource());
    }
    [Fact]
    public async Task Disposed_controller_cannot_overwrite_the_reopened_journal_after_a_late_start_response()
    {
        using var files = new Files(); var server = new Uri("https://example.test");
        var old = GuidedWorkspaceMigration.Create(server, "owner", files.Source, files.Private); old.Prepare(new HashSet<Guid>());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new WorkspaceMigrationStatus(old.Journal.CommandId, WorkspaceMigrationPhase.Prepared, old.Journal.ArchiveBytes, old.Journal.ArchiveSha256!, 524288, [0], null, [], null, null, null, []);
        var client = await Client(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("capabilities")) return Response(new WorkspaceMigrationCapabilities(1, 524288, 4294967296));
            started.TrySetResult(); await release.Task; return Response(state);
        });
        var transfer = old.TransferAsync(client, null, default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var path = old.JournalPath; old.Dispose();
        using var reopened = GuidedWorkspaceMigration.Load(path, server, "owner", files.Source);
        reopened.Prepare(new HashSet<Guid>()); var saved = File.ReadAllBytes(path);
        release.SetResult(); await Assert.ThrowsAsync<ObjectDisposedException>(() => transfer);
        Assert.Equal(saved, File.ReadAllBytes(path));
    }
    private static bool GuidedJournalHasIntent(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("ApplyRequested").GetBoolean();
    private static WorkspaceMigrationStatus Status(Guid id, byte[] bytes) => new(id, WorkspaceMigrationPhase.Uploading, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), 524288, [], null, [], null, null, null, []);
    private static async Task<RemoteWorkspaceClient> Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var client = new RemoteWorkspaceClient(new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("login") ? Task.FromResult(Response(new { accessToken = "token", expiresIn = 3600 })) : send(r))), new Uri("https://example.test")); await client.LoginAsync("owner", "secret"); return client;
    }
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } }) };
    private sealed class Progress(List<long> values) : IProgress<long> { public void Report(long value) => values.Add(value); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
    private sealed class Files : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "guided-migration-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_directory, "pc"); public string Private => Path.Combine(_directory, "private"); public string Document => Path.Combine(Source, "01_CLIENTS", "demo.txt");
        public Files() { Directory.CreateDirectory(Path.GetDirectoryName(Document)!); Directory.CreateDirectory(Private); File.WriteAllText(Document, "original bytes"); }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}

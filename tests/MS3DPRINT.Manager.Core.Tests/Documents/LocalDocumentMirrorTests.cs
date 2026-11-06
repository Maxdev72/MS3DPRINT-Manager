using MS3DPRINT.Manager.Core.Documents;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Tests.Documents;

public sealed class LocalDocumentMirrorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-mirror-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _workspace = Guid.NewGuid();
    private readonly Guid _storage = Guid.NewGuid();
    private readonly LocalDocumentMirror _mirror = new();

    public LocalDocumentMirrorTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private LocalWorkspaceBinding Binding(string? root = null) => new(_workspace, "owner", new Uri("https://one.example"), _storage, root ?? _root);
    private void Mark() => new WorkspaceDocumentMarker(1, _workspace, _storage, Guid.NewGuid()).WriteTo(_root);
    private void Write(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task An_empty_ordinary_folder_is_not_a_mirror()
    {
        var result = await _mirror.CheckAsync(Binding(), CancellationToken.None);
        Assert.NotEqual(MirrorAvailability.Complete, result.Availability);
    }

    [Fact]
    public async Task A_missing_folder_is_incomplete_and_never_created()
    {
        var missing = Path.Combine(_root, "absent");
        var result = await _mirror.CheckAsync(Binding(missing), CancellationToken.None);
        Assert.Equal(MirrorAvailability.Incomplete, result.Availability);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task The_marker_of_another_workspace_is_refused_and_kept()
    {
        new WorkspaceDocumentMarker(1, Guid.NewGuid(), _storage, Guid.NewGuid()).WriteTo(_root);
        var result = await _mirror.CheckAsync(Binding(), CancellationToken.None);
        Assert.Equal(MirrorAvailability.Incomplete, result.Availability);
        Assert.Throws<InvalidOperationException>(() => new WorkspaceDocumentMarker(1, _workspace, _storage, Guid.NewGuid()).WriteTo(_root));
        Assert.NotEqual(_workspace, WorkspaceDocumentMarker.Read(_root)!.WorkspaceId);
    }

    [Fact]
    public async Task All_expected_documents_present_and_readable_is_complete()
    {
        Mark();
        Write("CLIENTS/AE/devis.pdf");
        Write("CLIENTS/AE/piece.3mf");
        Write("bin/ignored.dll");

        var result = await _mirror.CheckAsync(Binding(), ["CLIENTS/AE/devis.pdf", @"CLIENTS\AE\piece.3mf"], CancellationToken.None);

        Assert.Equal(MirrorAvailability.Complete, result.Availability);
        Assert.Equal(2, result.VerifiedFiles);
    }

    [Fact]
    public async Task Without_a_manifest_a_clean_folder_is_unknown_not_complete()
    {
        Mark();
        Write("a.pdf");
        Assert.Equal(MirrorAvailability.Unknown, (await _mirror.CheckAsync(Binding(), CancellationToken.None)).Availability);
    }

    [Fact]
    public async Task A_missing_expected_document_or_a_path_outside_the_root_is_reported()
    {
        Mark();
        Write("a.pdf");
        var result = await _mirror.CheckAsync(Binding(), ["a.pdf", "b.pdf", "../hors.pdf"], CancellationToken.None);
        Assert.Equal(MirrorAvailability.Incomplete, result.Availability);
        Assert.Contains(result.Anomalies, a => a.RelativePath == "b.pdf");
        Assert.Contains(result.Anomalies, a => a.RelativePath == "../hors.pdf");
    }

    [Fact]
    public async Task A_virtual_file_not_downloaded_is_incomplete()
    {
        if (!OperatingSystem.IsWindows()) return;
        Mark();
        Write("a.pdf");
        File.SetAttributes(Path.Combine(_root, "a.pdf"), FileAttributes.Offline);
        var result = await _mirror.CheckAsync(Binding(), ["a.pdf"], CancellationToken.None);
        Assert.Equal(MirrorAvailability.Incomplete, result.Availability);
        Assert.Contains(result.Anomalies, a => a.RelativePath == "a.pdf");
    }

    [Fact]
    public async Task A_locked_file_is_reported_unreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        Mark();
        Write("a.pdf");
        using var exclusive = new FileStream(Path.Combine(_root, "a.pdf"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await _mirror.CheckAsync(Binding(), ["a.pdf"], CancellationToken.None);
        Assert.Equal(MirrorAvailability.Incomplete, result.Availability);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        Mark();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _mirror.CheckAsync(Binding(), cancelled.Token));
    }

    [Fact]
    public void Folders_are_recognised_by_their_marker_not_their_name()
    {
        Mark();
        var other = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms3d-mirror-other-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Assert.Equal([_root], LocalDocumentMirror.MatchingRoots([other, _root, _root + Path.DirectorySeparatorChar], _workspace, _storage));
            Assert.Empty(LocalDocumentMirror.MatchingRoots([_root], _workspace, Guid.NewGuid()));
        }
        finally { Directory.Delete(other, true); }
    }

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    public void Up_to_date_needs_both_circuits(bool data, bool mirror, bool remote, bool uncertain, bool expected)
    {
        var facts = new SyncFacts(data, mirror, remote, uncertain);
        Assert.Equal(expected, WorkspaceSyncStatus.IsUpToDate(facts));
        Assert.Equal(expected, WorkspaceSyncStatus.Describe(facts) == "Tout est à jour");
    }
}

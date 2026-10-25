using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateDraftWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-draft-writer-" + Guid.NewGuid().ToString("N"));
    public EstimateDraftWriterTests() => Directory.CreateDirectory(_root);
    private static EstimateDraft Draft() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, new() { ["Name"] = "Brouillon" }, 2, 0, 0, null, null, null, null, new(), true, false, [], [], null, null, null);

    [Fact]
    public void Older_or_unchanged_snapshots_never_replace_the_latest_saved_text()
    {
        var writer = new EstimateDraftWriter(); var draft = Draft();
        writer.Save(_root, draft with { Fields = new() { ["Name"] = "Dernier texte" } }, 3);
        writer.Save(_root, draft, 2); writer.Save(_root, draft, 3);
        Assert.Equal("Dernier texte", Assert.Single(new EstimateDraftStore(_root).LoadReadable()).Fields["Name"]);
    }

    [Fact]
    public void Failure_does_not_mark_a_revision_as_saved_and_can_be_retried()
    {
        var fail = true;
        var writer = new EstimateDraftWriter((root, draft) => { if (fail) throw new IOException("Disque indisponible"); new EstimateDraftStore(root).Save(draft); });
        var draft = Draft();
        Assert.Throws<IOException>(() => writer.Save(_root, draft, 1));
        Assert.False(writer.IsSaved(_root, draft.Id, 1));
        fail = false; writer.Save(_root, draft, 1);
        Assert.True(writer.IsSaved(_root, draft.Id, 1));
        Assert.Single(new EstimateDraftStore(_root).LoadReadable());
    }

    [Fact]
    public async Task Removal_waits_for_a_running_write_and_prevents_late_resurrection()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new EstimateDraftWriter((root, draft) =>
        {
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            new EstimateDraftStore(root).Save(draft);
        });
        var draft = Draft();
        var pending = Task.Run(() => writer.Save(_root, draft, 1));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var removal = Task.Run(() => writer.Remove(_root, draft.Id));
            release.Set(); await Task.WhenAll(pending, removal);
            writer.Save(_root, draft, 2);
            Assert.Empty(new EstimateDraftStore(_root).LoadReadable());
        }
        finally { release.Set(); await pending; }
    }

    [Fact]
    public void Unavailable_workspace_is_not_recreated_by_the_worker()
    {
        var missing = Path.Combine(_root, "absent");
        Assert.Throws<DirectoryNotFoundException>(() => new EstimateDraftWriter().Save(missing, Draft(), 1));
        Assert.False(Directory.Exists(missing));
    }
    public void Dispose() => Directory.Delete(_root, true);
}

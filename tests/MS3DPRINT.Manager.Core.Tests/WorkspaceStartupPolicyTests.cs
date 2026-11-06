using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class WorkspaceStartupPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-startup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    // facts: session, network, verifiedCopy, selected, bound, mirrorComplete, recovery
    [Theory]
    [InlineData(false, false, true, true, true, true, false, WorkspaceStartupState.ReadyOfflineReadOnly)]
    [InlineData(false, false, false, false, false, false, false, WorkspaceStartupState.NeedsSignIn)] // new device offline
    [InlineData(false, true, true, true, true, true, false, WorkspaceStartupState.NeedsSignIn)]      // expired session online
    [InlineData(true, true, false, false, false, false, false, WorkspaceStartupState.NeedsWorkspaceSelection)]
    [InlineData(true, true, false, true, false, false, false, WorkspaceStartupState.NeedsDocumentsBinding)]
    [InlineData(true, true, true, true, true, false, false, WorkspaceStartupState.HydratingDocuments)]
    [InlineData(true, true, true, true, true, true, false, WorkspaceStartupState.ReadyOnline)]
    [InlineData(true, true, true, true, true, true, true, WorkspaceStartupState.RecoveryRequired)]
    public void Startup_state_follows_the_facts(bool session, bool network, bool copy, bool selected, bool bound, bool mirror, bool recovery, WorkspaceStartupState expected)
        => Assert.Equal(expected, WorkspaceStartupPolicy.Resolve(new(session, network, copy, selected, bound, mirror, recovery)));

    [Fact]
    public void No_state_ever_offers_a_writable_local_workspace_offline()
    {
        foreach (var bits in Enumerable.Range(0, 128))
        {
            bool B(int i) => (bits & (1 << i)) != 0;
            var state = WorkspaceStartupPolicy.Resolve(new(B(0), false, B(2), B(3), B(4), B(5), B(6)));
            Assert.Contains(state, new[] { WorkspaceStartupState.ReadyOfflineReadOnly, WorkspaceStartupState.NeedsSignIn, WorkspaceStartupState.RecoveryRequired });
        }
    }

    private AccountWorkspacePaths Paths(string account = "owner", Guid? id = null)
        => new(_root, new(new Uri("https://one.example"), account, id ?? WorkspaceId));
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public void A_binding_is_kept_and_never_replaced_by_another_workspace()
    {
        var store = new WorkspaceBindingStore(Paths());
        var storage = Guid.NewGuid();
        var docs = Directory.CreateDirectory(Path.Combine(_root, "docs")).FullName;
        store.Save(new(WorkspaceId, "owner", new Uri("https://one.example"), storage, docs + Path.DirectorySeparatorChar));

        Assert.Equal(docs, new WorkspaceBindingStore(Paths()).Load()!.LocalDocumentsRoot);
        store.Save(new(WorkspaceId, "owner", new Uri("https://one.example"), storage, Path.Combine(_root, "D", "ailleurs")));
        Assert.Throws<InvalidOperationException>(() => store.Save(new(Guid.NewGuid(), "owner", new Uri("https://one.example"), storage, docs)));
        Assert.Throws<InvalidOperationException>(() => store.Save(new(WorkspaceId, "owner", new Uri("https://one.example"), Guid.NewGuid(), docs)));
        Assert.Equal(Path.Combine(_root, "D", "ailleurs"), store.Load()!.LocalDocumentsRoot);
    }

    [Fact]
    public void Another_account_does_not_see_the_binding_or_the_offline_authorisation()
    {
        var owner = new WorkspaceBindingStore(Paths());
        owner.Save(new(WorkspaceId, "owner", new Uri("https://one.example"), Guid.NewGuid(), _root));
        owner.GrantOfflineAfterSignIn();

        var other = new WorkspaceBindingStore(Paths("other"));
        Assert.Null(other.Load());
        Assert.False(other.IsOfflineAuthorized);
    }

    [Fact]
    public void Signing_out_removes_offline_reading_but_keeps_the_binding()
    {
        var store = new WorkspaceBindingStore(Paths());
        store.Save(new(WorkspaceId, "owner", new Uri("https://one.example"), Guid.NewGuid(), _root));
        store.GrantOfflineAfterSignIn();
        Assert.True(store.IsOfflineAuthorized);

        store.RevokeOfflineOnSignOut();

        Assert.False(new WorkspaceBindingStore(Paths()).IsOfflineAuthorized);
        Assert.NotNull(store.Load());
        Assert.Equal(WorkspaceStartupState.NeedsSignIn,
            WorkspaceStartupPolicy.Resolve(new(false, false, store.IsOfflineAuthorized, true, true, true, false)));
    }

    [Fact]
    public void An_unreadable_binding_is_treated_as_absent()
    {
        var paths = Paths();
        Directory.CreateDirectory(paths.DirectoryPath);
        File.WriteAllText(paths.BindingPath, "{ abîmé");
        Assert.Null(new WorkspaceBindingStore(paths).Load());
    }
}

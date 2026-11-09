using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tasks;

/// <summary>PC ⇄ server for independent tasks: each side keeps the copy with the latest date, removals included.</summary>
public static class RemoteTaskSync
{
    public static async Task<ProfileSyncResult> RunAsync(string root, IReadOnlyList<WorkTask> server, Func<WorkTask, Task<WorkTask>> put)
    {
        TaskMigration.FromProjects(root);
        var store = new TaskStore(new WorkspaceMetadataPaths(root));
        int pulled = 0, pushed = 0, failed = 0;
        var remote = new Dictionary<Guid, WorkTask>();
        foreach (var task in server) remote.TryAdd(task.Id, task);
        foreach (var task in remote.Values)
        {
            try { if (store.Merge(task)) pulled++; }
            catch (ArgumentException) { failed++; } // a malformed server copy is skipped, never written
        }
        foreach (var local in store.LoadAll())
        {
            if (remote.TryGetValue(local.Id, out var known) && known.UpdatedAt >= local.UpdatedAt) continue;
            try
            {
                var kept = await put(local).ConfigureAwait(false);
                // The server answers with what it holds: a newer copy made elsewhere comes back here.
                if (kept.UpdatedAt > local.UpdatedAt && store.Merge(kept)) pulled++;
                else pushed++;
            }
            catch (RemoteWorkspaceException) { failed++; }
        }
        return new ProfileSyncResult(pulled, pushed, failed);
    }

    /// <summary>Lists (empty ones included): merge the server's, then send ours when the server lacks a newer state.</summary>
    public static async Task<ProfileSyncResult> RunListsAsync(string root, IReadOnlyList<TaskListEntry> server, Func<IReadOnlyList<TaskListEntry>, Task<IReadOnlyList<TaskListEntry>>> put)
    {
        var store = new TaskListStore(new WorkspaceMetadataPaths(root));
        var pulled = store.Merge(server);
        var remote = server.Where(e => e is not null).GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.Max(e => e.UpdatedAt), StringComparer.CurrentCultureIgnoreCase);
        var local = store.LoadAll();
        if (!local.Any(e => !remote.TryGetValue(e.Name, out var at) || at < e.UpdatedAt)) return new(pulled, 0, 0);
        try { pulled += store.Merge(await put(local).ConfigureAwait(false)); return new(pulled, 1, 0); }
        catch (RemoteWorkspaceException) { return new(pulled, 0, 1); }
    }
}

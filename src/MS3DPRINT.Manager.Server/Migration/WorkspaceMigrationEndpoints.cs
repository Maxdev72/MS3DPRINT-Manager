using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Server.Workspace;

namespace MS3DPRINT.Manager.Server.Migration;

public static class WorkspaceMigrationEndpoints
{
    public static void MapWorkspaceMigrations(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/workspace-migrations").RequireAuthorization();
        WorkspaceEndpoints.ProtectMutations(group);
        group.MapGet("/capabilities", () => TypedResults.Ok(new WorkspaceMigrationCapabilities(1, WorkspaceMigrationStore.ChunkBytes, WorkspaceMigrationStore.MaxArchiveBytes)));
        group.MapPost("/operations", (StartWorkspaceMigration input, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(input.CommandId, () => Task.FromResult(store.Start(input)), token));
        group.MapGet("/operations/{id:guid}", (Guid id, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(id, () => Task.FromResult(store.Status(id)), token));
        group.MapPut("/operations/{id:guid}/parts/{index:int}", (Guid id, int index, HttpContext context, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(id, () => store.Part(id, index, context.Request, token), token))
            .WithMetadata(new MigrationBodyLimit(WorkspaceMigrationStore.ChunkBytes));
        group.MapPost("/operations/{id:guid}/prepare", (Guid id, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(id, () => Task.FromResult(store.Prepare(id)), token));
        group.MapPut("/operations/{id:guid}/choices", (Guid id, WorkspaceReconciliationChoice[] input, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(id, () => Task.FromResult(input is null ? Results.BadRequest() : store.Choices(id, input)), token))
            .WithMetadata(new MigrationBodyLimit(WorkspaceMigrationStore.ChoicesBodyBytes));
        group.MapPost("/operations/{id:guid}/apply", (Guid id, ApplyWorkspaceMigration input, WorkspaceMigrationStore store, CancellationToken token)
            => store.Locked(id, () => Task.FromResult(store.Apply(id, input)), token));
    }
}

public sealed record MigrationBodyLimit(long Bytes);

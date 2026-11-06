using System.Security.Claims;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Server.Workspace;

public static class WorkspaceIdentityEndpoints
{
    public const int SchemaVersion = 1;
    // Only what the routes of WorkspaceEndpoints really serve. Add a module only once it is ported.
    public static readonly IReadOnlyList<string> Modules = ["clients", "projects", "tasks"];

    internal static void MapWorkspaceIdentity(this RouteGroupBuilder group)
    {
        group.MapGet("/workspace", (ClaimsPrincipal user, WorkspaceRegistry registry, CommandStore receipts) =>
        {
            var workspace = registry.For(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok(new WorkspaceDescriptor(workspace.WorkspaceId, workspace.AccountId, workspace.Name,
                SchemaVersion, receipts.Revision().Revision, Documents: null));
        });
        group.MapGet("/workspace/capabilities", () => Results.Ok(new WorkspaceCapabilities(SchemaVersion, Modules, DocumentProtocol: null)));
        group.MapGet("/workspace/revision", (CommandStore receipts) => Results.Ok(receipts.Revision()));
    }
}

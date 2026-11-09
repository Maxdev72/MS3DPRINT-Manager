using Microsoft.AspNetCore.Antiforgery;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Server.Workspace;

public static class WorkspaceEndpoints
{
    public static void MapWorkspace(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        ProtectMutations(group);
        group.MapWorkspaceIdentity();
        MapRoutes(group);
    }

    internal static void ProtectMutations(RouteGroupBuilder group) => group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (HttpMethods.IsPost(http.Request.Method) || HttpMethods.IsPut(http.Request.Method))
            {
                if (http.User.FindFirst("transport")?.Value == "cookie")
                {
                    try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
                }
            }
            try { return await next(context); }
            catch (WorkspaceUnavailableException) { return Results.StatusCode(503); }
        });

    private static void MapRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/clients", (CommandStore receipts, WorkspaceReader reader) => receipts.Read(() => Results.Ok(reader.ReadClients())));
        group.MapGet("/projects", (CommandStore receipts, WorkspaceReader reader) => receipts.Read(() => Results.Ok(reader.ReadProjects())));
        group.MapGet("/clients/{id:guid}", (Guid id, CommandStore receipts, WorkspaceCommands commands) => Detail(receipts, () => commands.Client(id)));
        group.MapGet("/projects/{id:guid}", (Guid id, CommandStore receipts, WorkspaceCommands commands) => Detail(receipts, () => commands.Project(id)));
        group.MapGet("/tasks", (CommandStore receipts, WorkspaceReader reader) => receipts.Read(() => Results.Ok(reader.ReadTasks())));
        // Independent tasks sync by date: the latest copy wins, so sending the same task twice is harmless.
        group.MapPut("/tasks/{id:guid}", (Guid id, MS3DPRINT.Manager.Core.Tasks.WorkTask input, CommandStore receipts, WorkspaceReader reader)
            => receipts.Read(() => reader.MergeTask(id, input)));
        group.MapGet("/task-lists", (CommandStore receipts, WorkspaceReader reader) => receipts.Read(() => Results.Ok(reader.ReadTaskLists())));
        group.MapPut("/task-lists", (MS3DPRINT.Manager.Core.Tasks.TaskListEntry[] input, CommandStore receipts, WorkspaceReader reader)
            => receipts.Read(() => reader.MergeTaskLists(input)));
        group.MapGet("/commands/{id:guid}", (Guid id, CommandStore receipts) => id == Guid.Empty ? Results.BadRequest() : receipts.Receipt(id) is { } receipt ? Results.Ok(receipt) : Results.NotFound());
        group.MapPost("/clients", (CreateClientCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.Create(input)));
        group.MapPut("/clients/{id:guid}", (Guid id, UpdateClientCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.Update(id, input)));
        group.MapPost("/projects", (CreateProjectCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.Create(input)));
        group.MapPut("/projects/{id:guid}", (Guid id, UpdateProjectCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.Update(id, input)));
        group.MapPost("/projects/{id:guid}/tasks", (Guid id, AddTaskCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.AddTask(id, input)));
        group.MapPut("/projects/{id:guid}/tasks/{taskId:guid}", (Guid id, Guid taskId, EditTaskCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.EditTask(id, taskId, input)));
        group.MapPost("/projects/{id:guid}/tasks/{taskId:guid}/complete", (Guid id, Guid taskId, CompleteTaskCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.CompleteTask(id, taskId, input)));
        group.MapPost("/projects/{id:guid}/tasks/{taskId:guid}/move", (Guid id, Guid taskId, MoveTaskCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.MoveTask(id, taskId, input)));
        group.MapPost("/projects/{id:guid}/tasks/{taskId:guid}/remove", (Guid id, Guid taskId, RemoveTaskCommand input, HttpRequest request, CommandStore receipts, WorkspaceCommands commands)
            => receipts.Execute(input.CommandId, request, input, () => commands.RemoveTask(id, taskId, input)));
    }

    private static IResult Detail<T>(CommandStore receipts, Func<T> load) => receipts.Read<IResult>(() =>
    {
        try { return Results.Ok(load()); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException) { return Results.BadRequest(); }
    });
}

using System.Text.Json;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Remote;

public sealed record CreateClientCommand(Guid CommandId, ClientKind Kind, string ClientCode,
    string? CompanyName, string? FirstName, string? LastName, string? Address,
    string? Notes, PrimaryContact Contact);
public sealed record UpdateClientCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt,
    ClientKind Kind, string? CompanyName, string? FirstName, string? LastName,
    string? Address, string? Notes, PrimaryContact Contact);
public sealed record CreateProjectCommand(Guid CommandId, Guid ClientId, string ProjectName,
    int Year, DateOnly? DueDate, string? Description, ProjectPriority Priority);
public sealed record UpdateProjectCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt,
    ProjectStatus Status, DateOnly? DueDate, string? Description, string? Notes, ProjectPriority Priority,
    IReadOnlyList<ProjectTask>? Tasks = null);
public sealed record AddTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, string Title, string? Notes);
public sealed record EditTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, string Title, string? Notes);
public sealed record CompleteTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, bool Completed);
public sealed record MoveTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, int Direction);
public sealed record RemoveTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, bool Confirmed);

public sealed record ClientResponse(Guid Id, string Kind, string ClientCode, string DisplayName, DateTimeOffset UpdatedAt,
    string? CompanyName = null, string? FirstName = null, string? LastName = null, string? Address = null, string? Notes = null,
    PrimaryContact? Contact = null);
public sealed record ProjectResponse(Guid Id, Guid ClientId, string Reference, string ProjectName, string Status,
    DateOnly? DueDate, string Priority, DateTimeOffset UpdatedAt, IReadOnlyList<ProjectTask> Tasks,
    string? Description = null, string? Notes = null, bool FullSheet = false);
public sealed record ClientDetailResponse(Guid Id, ClientKind Kind, string FolderName, string ClientCode,
    string? CompanyName, string? FirstName, string? LastName, string? Address, string? Notes,
    PrimaryContact PrimaryContact, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Siret);
public sealed record ProjectDetailResponse(Guid Id, Guid ClientId, string ClientCode, string Reference,
    string FolderName, string ProjectName, ProjectStatus Status, DateTimeOffset CreatedAt,
    DateOnly? DueDate, string? Description, string? Notes, DateTimeOffset UpdatedAt,
    ProjectPriority Priority, IReadOnlyList<ProjectTask> Tasks);
public sealed record CommandReceiptResponse(Guid CommandId, string State, int? StatusCode, JsonElement? Result);

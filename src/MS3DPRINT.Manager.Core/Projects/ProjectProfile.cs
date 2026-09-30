namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectProfile(
    Guid Id,
    Guid ClientId,
    string ClientCode,
    string Reference,
    string FolderName,
    string ProjectName,
    ProjectStatus Status,
    DateTimeOffset CreatedAt,
    DateOnly? DueDate,
    string? Description,
    string? Notes,
    DateTimeOffset UpdatedAt);

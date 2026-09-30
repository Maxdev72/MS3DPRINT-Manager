namespace MS3DPRINT.Manager.Core.Clients;

public sealed record ClientProfile(
    Guid Id,
    ClientKind Kind,
    string FolderName,
    string ClientCode,
    string? CompanyName,
    string? FirstName,
    string? LastName,
    string? Address,
    string? Notes,
    PrimaryContact PrimaryContact,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

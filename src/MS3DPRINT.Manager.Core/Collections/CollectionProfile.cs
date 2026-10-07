namespace MS3DPRINT.Manager.Core.Collections;

public sealed record CollectionProfile(Guid Id, string Category, string RelativePath, string Name,
    string? Description, string? Contact, string? Address, string? Phone, string? Email,
    string? Website, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

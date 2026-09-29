namespace MS3DPRINT.Manager.Core.Storage;

public sealed record StorageProviderDefinition(
    StorageProviderKind Kind,
    string DisplayName,
    bool IsAvailable,
    string? RootPath);

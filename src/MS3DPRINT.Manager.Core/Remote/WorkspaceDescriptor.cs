namespace MS3DPRINT.Manager.Core.Remote;

/// <summary>Public identity of the authenticated workspace. Never carries a host path or a provider secret.</summary>
public sealed record WorkspaceDescriptor(Guid WorkspaceId, string AccountId,
    string Name, int SchemaVersion, long Revision, DocumentStorageDescriptor? Documents);

public sealed record DocumentStorageDescriptor(Guid StorageBindingId,
    string Provider, string ProviderInstanceId, string WorkspaceMarkerId);

/// <summary>Modules the server really serves; a module missing here is not synchronised.</summary>
public sealed record WorkspaceCapabilities(int SchemaVersion, IReadOnlyList<string> Modules, string? DocumentProtocol);

/// <summary><paramref name="Verified"/> is false while a command has an uncertain result: never show « à jour » then.</summary>
public sealed record WorkspaceRevisionResponse(long Revision, bool Verified);

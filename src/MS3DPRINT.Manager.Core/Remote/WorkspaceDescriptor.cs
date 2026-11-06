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

/// <summary>This device's link between a workspace and its local Nextcloud folder; the absolute root never leaves the device.</summary>
public sealed record LocalWorkspaceBinding(Guid WorkspaceId, string AccountId,
    Uri Server, Guid StorageBindingId, string LocalDocumentsRoot);

public sealed record DocumentLink(Guid DocumentId, Guid WorkspaceId,
    Guid StorageBindingId, Guid? ClientId, Guid? ProjectId, string RelativePath,
    string? ProviderFileId, string? RemoteEtag, long Bytes,
    string? VerifiedSha256, string State, DateTimeOffset ObservedAt);

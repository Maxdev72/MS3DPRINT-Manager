namespace MS3DPRINT.Manager.Core.Remote;

/// <summary>Who owns a local copy: the server origin, the authenticated account and the workspace GUID.</summary>
public sealed record WorkspaceIdentity(Uri Server, string AccountId, Guid WorkspaceId);

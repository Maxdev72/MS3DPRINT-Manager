namespace MS3DPRINT.Manager.Desktop;

public enum WorkspaceStartupState
{
    NeedsSignIn, NeedsWorkspaceSelection, NeedsDocumentsBinding,
    HydratingDocuments, ReadyOnline, ReadyOfflineReadOnly, RecoveryRequired
}

/// <summary><c>HasVerifiedCopy</c> means a copy of this identity plus a local authorisation from a successful sign-in.</summary>
public sealed record StartupFacts(bool HasAuthenticatedSession, bool NetworkAvailable,
    bool HasVerifiedCopy, bool WorkspaceSelected, bool DocumentsBound,
    bool LocalMirrorComplete, bool RecoveryRequired);

/// <summary>Never falls back to a writable local workspace: offline is read-only, and only for an already verified copy.</summary>
public static class WorkspaceStartupPolicy
{
    public static WorkspaceStartupState Resolve(StartupFacts facts)
    {
        if (facts.RecoveryRequired) return WorkspaceStartupState.RecoveryRequired;
        if (!facts.NetworkAvailable) return facts.HasVerifiedCopy
            ? WorkspaceStartupState.ReadyOfflineReadOnly : WorkspaceStartupState.NeedsSignIn;
        if (!facts.HasAuthenticatedSession) return WorkspaceStartupState.NeedsSignIn;
        if (!facts.WorkspaceSelected) return WorkspaceStartupState.NeedsWorkspaceSelection;
        if (!facts.DocumentsBound) return WorkspaceStartupState.NeedsDocumentsBinding;
        return facts.LocalMirrorComplete ? WorkspaceStartupState.ReadyOnline
            : WorkspaceStartupState.HydratingDocuments;
    }
}

namespace MS3DPRINT.Manager.Core.Documents;

public sealed record SyncFacts(bool DataCurrent, bool LocalMirrorComplete,
    bool RemoteDocumentsVerified, bool HasUncertainCommand);

/// <summary>« Tout est à jour » needs both circuits confirmed: records and documents. Otherwise the exact cause is shown.</summary>
public static class WorkspaceSyncStatus
{
    public static bool IsUpToDate(SyncFacts facts) => facts.DataCurrent
        && facts.LocalMirrorComplete && facts.RemoteDocumentsVerified
        && !facts.HasUncertainCommand;

    public static string Describe(SyncFacts facts) =>
        facts.HasUncertainCommand ? "À vérifier : une modification a un résultat incertain."
        : !facts.DataCurrent ? "Vérification des données en cours."
        : !facts.LocalMirrorComplete ? "Documents incomplets sur ce PC."
        : !facts.RemoteDocumentsVerified ? "Vérification des documents."
        : "Tout est à jour";
}

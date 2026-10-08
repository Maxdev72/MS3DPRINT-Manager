namespace MS3DPRINT.Manager.Core.Workspace;

/// <summary>Preserves a failed creation in recoverable trash instead of leaving it active.</summary>
internal static class CreationRollback
{
    public static void Preserve(string root, string createdDirectory, IReadOnlyList<string> createdProfiles, Exception failure)
    {
        try
        {
            new ManagedFileService(root).Trash(createdDirectory, createdProfiles,
                "Création interrompue — " + Path.GetFileName(createdDirectory));
        }
        catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new AggregateException("La création a échoué et ses données sont conservées à cet emplacement : " + createdDirectory,
                failure, rollbackFailure);
        }
    }
}

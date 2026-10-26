namespace MS3DPRINT.Manager.Core.Storage;

/// <summary>Replaces a file by another one. The workspace lives in a synchronised folder: Nextcloud, the antivirus or the indexer may hold the file for a moment, which makes File.Replace fail with "Impossible de supprimer le fichier à remplacer". The replacement is retried for a few seconds before giving up.</summary>
public static class AtomicFile
{
    private const int Attempts = 12;

    public static void Replace(string source, string destination, string? backup = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Replace(source, destination, backup); return; }
            catch (Exception exception) when (attempt < Attempts && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(Math.Min(100 * attempt, 500));
            }
        }
    }
}

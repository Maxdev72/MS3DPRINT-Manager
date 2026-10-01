namespace MS3DPRINT.Manager.Core.Files;

public sealed class ProjectFileTransferException : IOException
{
    public ProjectFileTransferException(string operation, string sourcePath, string destinationPath, Exception cause)
        : base($"Échec de {operation}.\nSource : {sourcePath}\nDestination : {destinationPath}\n" +
               $"Erreur système : {cause.Message}\nHRESULT : 0x{cause.HResult:X8}" +
               ((cause.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
                   ? $" (Win32 : {cause.HResult & 0xFFFF}).\n" : ".\n") +
               "Vérifiez les droits sur ces chemins, les fichiers ouverts et la disponibilité locale des fichiers Nextcloud.", cause)
    {
        Operation = operation;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        HResult = cause.HResult;
    }

    public string Operation { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public int? Win32ErrorCode => (HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
        ? HResult & 0xFFFF : null;
}

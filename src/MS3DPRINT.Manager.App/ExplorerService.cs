using System.Diagnostics;

namespace MS3DPRINT.Manager.App;

internal static class ExplorerService
{
    public static void Open(string path)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            if (process is null) throw new InvalidOperationException("L’Explorateur n’a pas démarré.");
        }
        catch (Exception exception)
        {
            throw new ExplorerOpenException(exception);
        }
    }
}

internal sealed class ExplorerOpenException : Exception
{
    public ExplorerOpenException(Exception innerException)
        : base("Impossible d’ouvrir le dossier dans l’Explorateur Windows.", innerException) { }
}

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Appends unexpected errors to a text file next to the window settings, to help diagnose them later.</summary>
public static class ErrorLog
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "erreurs.log");

    public static void Write(Exception exception, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // Logging must never raise a second error.
        }
    }
}

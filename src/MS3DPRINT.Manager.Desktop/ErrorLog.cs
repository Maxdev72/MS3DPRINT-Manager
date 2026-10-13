namespace MS3DPRINT.Manager.Desktop;

/// <summary>Appends unexpected errors to a text file next to the window settings, to help diagnose them later.</summary>
public static class ErrorLog
{
    /// <summary>Beyond this size the log is set aside as <c>erreurs.old.log</c> (replacing the previous one) and a fresh one is started.</summary>
    public const long MaxBytes = 1024 * 1024;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "erreurs.log");

    public static void Write(Exception exception, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".old" + Path.GetExtension(path)), overwrite: true);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // Logging must never raise a second error.
        }
    }
}

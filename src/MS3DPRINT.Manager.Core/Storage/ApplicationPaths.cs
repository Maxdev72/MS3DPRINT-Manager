namespace MS3DPRINT.Manager.Core.Storage;

public static class ApplicationPaths
{
    // Keep the existing portable Windows layout; Linux application directories can be read-only.
    public static string DataDirectory => OperatingSystem.IsLinux()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "data")
        : Path.Combine(AppContext.BaseDirectory, "data");
}

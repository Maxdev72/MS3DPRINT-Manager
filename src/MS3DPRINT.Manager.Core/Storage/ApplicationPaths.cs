namespace MS3DPRINT.Manager.Core.Storage;

public static class ApplicationPaths
{
    // Profile folder (Windows %LOCALAPPDATA%, Linux XDG_DATA_HOME): moving or replacing the executable keeps the settings.
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "data");

    /// <summary>The historical Windows folder next to the executable, only read by <see cref="LegacySettingsMigration"/>.</summary>
    public static string LegacyDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
}

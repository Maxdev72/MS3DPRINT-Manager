namespace MS3DPRINT.Manager.Desktop;

/// <summary>Only inspect the known business roots; never walk the whole workspace.</summary>
public static class WorkspaceStartupMode
{
    public static bool HasLocalBusinessData(string root)
    {
        foreach (var relative in MS3DPRINT.Manager.Core.Templates.FolderTemplates.Main.Append(".ms3dprint-manager"))
        {
            var path = Path.Combine(root, relative);
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) return true;
                if (Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Any()) return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return true; }
        }
        return false;
    }

    public static bool UseLocal(string root, string? explicitMode, bool explicitLocal, bool knownLocalUnavailable = false)
        => explicitLocal || explicitMode == "local" || explicitMode != "server" && (knownLocalUnavailable || HasLocalBusinessData(root));
}

namespace MS3DPRINT.Manager.Core.Files;

public static class ThreeDFileSupport
{
    public static bool IsPreviewable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase);
    }
}

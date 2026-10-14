namespace MS3DPRINT.Manager.Core.Files;

public static class ThreeDFileSupport
{
    public static bool IsPreviewable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);
        return extension.Equals(".stl", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".3mf", StringComparison.OrdinalIgnoreCase);
    }
}

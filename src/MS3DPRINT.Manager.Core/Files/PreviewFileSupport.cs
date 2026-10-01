namespace MS3DPRINT.Manager.Core.Files;

public enum PreviewFileKind { None, Image, Pdf }

public static class PreviewFileSupport
{
    public static PreviewFileKind GetKind(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" => PreviewFileKind.Image,
            ".pdf" => PreviewFileKind.Pdf,
            _ => PreviewFileKind.None
        };
    }
}

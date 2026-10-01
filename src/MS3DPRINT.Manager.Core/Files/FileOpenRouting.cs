namespace MS3DPRINT.Manager.Core.Files;

public enum FileOpenTarget { ThreeD, Document, External }

public static class FileOpenRouting
{
    public static FileOpenTarget Decide(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (ThreeDFileSupport.IsPreviewable(path)) return FileOpenTarget.ThreeD;
        if (PreviewFileSupport.GetKind(path) != PreviewFileKind.None) return FileOpenTarget.Document;
        return FileOpenTarget.External;
    }
}

using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Optional host bridge for document previews that need a platform renderer (PDF).</summary>
public static class DesktopPreviewLauncher
{
    public static Action<string>? OpenDocument { get; set; }

    /// <summary>Shows the platform "Open with" chooser for a file (Windows: the shell's "openas" dialog).</summary>
    public static Action<string>? OpenWith { get; set; }

    public static bool TryOpenDocument(string path)
    {
        if (PreviewFileSupport.GetKind(path) == PreviewFileKind.None || OpenDocument is null) return false;
        OpenDocument(path);
        return true;
    }
}

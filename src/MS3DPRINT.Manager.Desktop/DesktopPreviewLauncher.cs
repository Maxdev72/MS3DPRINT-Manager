using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Optional host bridge for native 3D previews without a Windows dependency in the shared desktop UI.</summary>
public static class DesktopPreviewLauncher
{
    public static Action<string?>? OpenThreeD { get; set; }

    public static bool TryOpenThreeD(string path)
    {
        if (!ThreeDFileSupport.IsPreviewable(path) || OpenThreeD is null) return false;
        OpenThreeD(path);
        return true;
    }
}

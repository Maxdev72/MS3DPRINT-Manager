using System.Diagnostics;

namespace MS3DPRINT.Manager.Linux;

internal static class LinuxShell
{
    public static void OpenWith(string path)
    {
        var start = new ProcessStartInfo("gio") { UseShellExecute = false };
        start.ArgumentList.Add("open");
        start.ArgumentList.Add(Path.GetFullPath(path));
        Process.Start(start);
    }
}

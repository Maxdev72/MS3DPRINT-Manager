using System.Diagnostics;

namespace MS3DPRINT.Manager.App;

internal static class ExplorerService
{
    public static void Open(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }
}

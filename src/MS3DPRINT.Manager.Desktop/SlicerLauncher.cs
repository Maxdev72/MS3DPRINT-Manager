using System.ComponentModel;
using System.Diagnostics;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.ModelSearch;
using MS3DPRINT.Manager.Core.Slicing;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Sends a 3D file to the slicer installed on this computer, for when Windows has no application for its type.</summary>
public static class SlicerLauncher
{
    /// <summary>
    /// Opens the file in the slicer found on this computer (Bambu Studio, else OrcaSlicer) and returns its name;
    /// null when there is none, or the file is not a 3D model.
    /// </summary>
    public static string? TryOpen(string path)
    {
        if (!DownloadBuffer.IsModelFile(path) || ExecutableFileGuard.IsExecutable(path) || !File.Exists(path)) return null;
        if (SlicerLocator.Detect() is not { } slicer) return null;
        try
        {
            var start = new ProcessStartInfo(slicer.ExecutablePath) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
            return slicer.Name;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

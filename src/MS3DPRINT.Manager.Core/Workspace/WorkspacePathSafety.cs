using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MS3DPRINT.Manager.Core.Workspace;

/// <summary>Rejects path redirection while permitting Windows Cloud Files placeholders.</summary>
public static class WorkspacePathSafety
{
    public static void EnsureNoLinks(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
            if (!OperatingSystem.IsWindows()) throw new IOException("Les liens ne sont pas autorisés.");
            using var handle = CreateFile(current, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Impossible de vérifier le point de réanalyse.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (GetFileInformationByHandleEx(handle, 9, out var info, 8) == 0)
                throw new IOException("Impossible de lire le type de point de réanalyse.", new Win32Exception(Marshal.GetLastWin32Error()));
            // CLOUD and CLOUD_1..F differ only in bits covered by CLOUD_MASK (0xF000).
            if ((info.ReparseTag & ~0x0000F000u) != 0x9000001Au)
                throw new IOException("Les liens, jonctions et points de réanalyse inconnus ne sont pas autorisés.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo { public uint FileAttributes; public uint ReparseTag; }

    // Runtime marshalling avoids changing the existing project's unsafe-build settings.
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileAttributeTagInfo information, uint bufferSize);
}

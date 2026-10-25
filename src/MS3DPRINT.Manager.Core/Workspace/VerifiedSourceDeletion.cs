using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MS3DPRINT.Manager.Core.Workspace;

/// <summary>Locks an original against replacement, then deletes the verified file by its open Windows handle.</summary>
internal static class VerifiedSourceDeletion
{
    public static FileStream Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new IOException("La suppression de la source est disponible sous Windows. La source est conservée.");
        // GENERIC_READ | DELETE, FILE_SHARE_READ only, OPEN_EXISTING.
        var handle = CreateFile(path, 0x80010000, 1, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new IOException("Impossible de verrouiller le fichier source. Il est conservé.", new Win32Exception(error));
        }
        try { return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    public static void MarkForDeletion(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (SetFileInformationByHandle(handle, 4, ref disposition, 1) == 0)
            throw new IOException("Impossible de supprimer le fichier source. La copie importée est conservée.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }

    // Match the existing runtime-marshalled Win32 interop without enabling unsafe compilation.
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDispositionInfo information, uint bufferSize);
}

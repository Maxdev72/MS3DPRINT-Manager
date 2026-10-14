using System.Runtime.InteropServices;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>
/// The Windows "Open with" window (SHOpenWithDialog). Unlike starting the file with the <c>openas</c> verb, it also works for a file
/// type that has no application yet: the user picks one, and may tick "always use this app".
/// </summary>
internal static class OpenWithDialog
{
    private const int AllowRegistration = 0x1;
    private const int ExecuteAfterChoice = 0x4;
    private const int Cancelled = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public int Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);

    /// <summary>Shows the window and opens the file with the chosen application; returns quietly when the user cancels.</summary>
    public static void Show(string path, IntPtr owner)
    {
        var info = new OpenAsInfo { File = path, Class = null, Flags = AllowRegistration | ExecuteAfterChoice };
        var result = SHOpenWithDialog(owner, ref info);
        if (result == 0 || result == Cancelled) return;
        throw new InvalidOperationException($"La fenêtre « Ouvrir avec » de Windows n’a pas pu s’ouvrir (erreur 0x{result:X8}).");
    }
}

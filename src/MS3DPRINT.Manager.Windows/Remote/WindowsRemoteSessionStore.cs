using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Windows.Remote;

/// <summary>One private native device session in Windows Credential Manager; never a password.</summary>
public sealed class WindowsRemoteSessionStore(string target = "MS3DPRINT.Manager/RemoteDeviceSession") : IRemoteSessionStore
{
    private const uint Generic = 1, LocalMachine = 2;
    private const int NotFound = 1168, MaximumBlobBytes = 2560;
    private readonly string _target = !string.IsNullOrWhiteSpace(target) && target.Length <= 32767 && !target.Any(char.IsControl)
        ? target : throw new ArgumentException("Cible du coffre invalide.", nameof(target));

    public RemoteDeviceSession? Load()
    {
        if (!CredRead(_target, Generic, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error);
        }
        using (handle)
        {
            var credential = Marshal.PtrToStructure<Credential>(handle.DangerousGetHandle());
            if (credential.CredentialBlobSize is 0 or > MaximumBlobBytes || credential.CredentialBlob == IntPtr.Zero)
                throw new InvalidDataException("Session mémorisée illisible.");
            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return JsonSerializer.Deserialize<RemoteDeviceSession>(bytes)
                    ?? throw new InvalidDataException("Session mémorisée vide.");
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            { throw new InvalidDataException("Session mémorisée invalide.", ex); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }

    public void Save(RemoteDeviceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        IntPtr blob = IntPtr.Zero;
        try
        {
            if (bytes.Length > MaximumBlobBytes) throw new InvalidDataException("Session trop volumineuse pour le coffre Windows.");
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = Generic, TargetName = _target, CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob, Persist = LocalMachine, UserName = session.Account
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blob != IntPtr.Zero)
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
        }
    }

    public void Clear()
    {
        if (!CredDelete(_target, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new Win32Exception(error);
        }
    }

    // Wincred owns the read allocation; the handle always releases it with CredFree.
    private sealed class CredentialHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private CredentialHandle() : base(true) { }
        protected override bool ReleaseHandle() { CredFree(handle); return true; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out CredentialHandle credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}

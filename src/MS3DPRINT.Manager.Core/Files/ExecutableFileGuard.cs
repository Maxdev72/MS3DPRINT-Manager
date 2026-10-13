namespace MS3DPRINT.Manager.Core.Files;

/// <summary>Spots files that Windows would run rather than open; project folders hold files received from clients.</summary>
public static class ExecutableFileGuard
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".cpl", ".jar", ".dll", ".msc", ".gadget", ".appx", ".msix", ".application"
    };

    public static bool IsExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Windows ignores trailing dots and spaces, so "setup.exe. " is still setup.exe.
        var trimmed = path.TrimEnd('.', ' ');
        return Extensions.Contains(Path.GetExtension(trimmed));
    }
}

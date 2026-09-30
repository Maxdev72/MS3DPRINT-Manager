using System.Management;

namespace MS3DPRINT.Manager.App.Hardware;

internal sealed class WindowsHardwareSnapshotReader : IHardwareSnapshotReader
{
    public HardwareSnapshot Read()
        => new(ReadFirstString("Win32_Processor", "Name"), ReadFirstUInt64("Win32_ComputerSystem", "TotalPhysicalMemory"), ReadAllStrings("Win32_VideoController", "Name"));

    private static string? ReadFirstString(string className, string property)
        => ReadAllStrings(className, property).FirstOrDefault();

    private static ulong? ReadFirstUInt64(string className, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (ManagementObject item in searcher.Get())
            {
                return ulong.TryParse(item[property]?.ToString(), out var value) ? value : null;
            }
        }
        catch
        {
            // The caller converts unavailable Windows Management Instrumentation data to a safe fallback.
        }

        return null;
    }

    private static IReadOnlyList<string?> ReadAllStrings(string className, string property)
    {
        var values = new List<string?>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (ManagementObject item in searcher.Get()) values.Add(item[property]?.ToString());
        }
        catch
        {
            // A missing or blocked WMI category must not block the settings dialog.
        }

        return values;
    }
}

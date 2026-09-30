namespace MS3DPRINT.Manager.App.Hardware;

public sealed record HardwareProfile(
    string ProcessorName,
    int LogicalProcessorCount,
    string TotalMemory,
    IReadOnlyList<string> GraphicsAdapters);

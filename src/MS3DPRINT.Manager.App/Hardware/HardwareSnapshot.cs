namespace MS3DPRINT.Manager.App.Hardware;

public sealed record HardwareSnapshot(
    string? ProcessorName,
    ulong? TotalMemoryBytes,
    IReadOnlyList<string?> GraphicsAdapters);

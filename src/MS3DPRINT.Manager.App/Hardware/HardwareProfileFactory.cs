namespace MS3DPRINT.Manager.App.Hardware;

public static class HardwareProfileFactory
{
    public static HardwareProfile Create(
        string? processorName,
        int logicalProcessorCount,
        ulong? totalMemoryBytes,
        IEnumerable<string?> graphicsAdapters)
    {
        ArgumentNullException.ThrowIfNull(graphicsAdapters);

        var adapters = graphicsAdapters
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new HardwareProfile(
            string.IsNullOrWhiteSpace(processorName) ? "Non détecté" : processorName.Trim(),
            Math.Max(1, logicalProcessorCount),
            totalMemoryBytes is > 0 ? $"{Math.Round(totalMemoryBytes.Value / 1_073_741_824d):0} Go" : "Non détecté",
            adapters.Length == 0 ? ["Non détecté"] : adapters);
    }

    public static HardwareProfile Fallback(int logicalProcessorCount)
        => Create(null, logicalProcessorCount, null, []);
}

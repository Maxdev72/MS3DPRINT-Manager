namespace MS3DPRINT.Manager.App.Hardware;

public sealed class WindowsHardwareInfoProvider : IHardwareInfoProvider
{
    private readonly Lazy<HardwareProfile> _profile;

    public WindowsHardwareInfoProvider()
        : this(new WindowsHardwareSnapshotReader(), Environment.ProcessorCount)
    {
    }

    public WindowsHardwareInfoProvider(IHardwareSnapshotReader reader, int logicalProcessorCount)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _profile = new Lazy<HardwareProfile>(() => ReadProfile(reader, logicalProcessorCount), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public HardwareProfile GetHardwareProfile() => _profile.Value;

    private static HardwareProfile ReadProfile(IHardwareSnapshotReader reader, int logicalProcessorCount)
    {
        try
        {
            var snapshot = reader.Read();
            return HardwareProfileFactory.Create(snapshot.ProcessorName, logicalProcessorCount, snapshot.TotalMemoryBytes, snapshot.GraphicsAdapters);
        }
        catch
        {
            return HardwareProfileFactory.Fallback(logicalProcessorCount);
        }
    }
}

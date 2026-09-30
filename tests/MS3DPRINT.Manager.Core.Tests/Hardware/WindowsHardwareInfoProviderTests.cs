using MS3DPRINT.Manager.App.Hardware;

namespace MS3DPRINT.Manager.Core.Tests.Hardware;

public sealed class WindowsHardwareInfoProviderTests
{
    [Fact]
    public void GetHardwareProfile_QueriesWindowsOnlyOnce()
    {
        var reader = new FakeHardwareSnapshotReader(new("CPU Test", 17_179_869_184, ["GPU Test"]));
        var provider = new WindowsHardwareInfoProvider(reader, logicalProcessorCount: 8);

        _ = provider.GetHardwareProfile();
        _ = provider.GetHardwareProfile();

        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public void GetHardwareProfile_ReturnsFallbackWhenWindowsQueryFails()
    {
        var provider = new WindowsHardwareInfoProvider(new ThrowingHardwareSnapshotReader(), logicalProcessorCount: 12);

        var profile = provider.GetHardwareProfile();

        Assert.Equal("Non détecté", profile.ProcessorName);
        Assert.Equal(12, profile.LogicalProcessorCount);
        Assert.Equal(["Non détecté"], profile.GraphicsAdapters);
    }

    private sealed class FakeHardwareSnapshotReader(HardwareSnapshot snapshot) : IHardwareSnapshotReader
    {
        public int ReadCount { get; private set; }

        public HardwareSnapshot Read()
        {
            ReadCount++;
            return snapshot;
        }
    }

    private sealed class ThrowingHardwareSnapshotReader : IHardwareSnapshotReader
    {
        public HardwareSnapshot Read() => throw new InvalidOperationException("WMI indisponible");
    }
}

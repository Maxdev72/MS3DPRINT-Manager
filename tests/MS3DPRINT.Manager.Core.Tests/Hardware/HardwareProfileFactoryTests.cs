using MS3DPRINT.Manager.App.Hardware;

namespace MS3DPRINT.Manager.Core.Tests.Hardware;

public sealed class HardwareProfileFactoryTests
{
    [Fact]
    public void Create_NormalizesMissingHardwareFields()
    {
        var profile = HardwareProfileFactory.Create(null, 0, null, [null, "  "]);

        Assert.Equal("Non détecté", profile.ProcessorName);
        Assert.Equal(1, profile.LogicalProcessorCount);
        Assert.Equal("Non détecté", profile.TotalMemory);
        Assert.Equal(["Non détecté"], profile.GraphicsAdapters);
    }

    [Fact]
    public void Create_FormatsMemoryAndKeepsDetectedGraphicsAdapters()
    {
        var profile = HardwareProfileFactory.Create("CPU Test", 16, 34_359_738_368, ["GPU A", "GPU B"]);

        Assert.Equal("CPU Test", profile.ProcessorName);
        Assert.Equal(16, profile.LogicalProcessorCount);
        Assert.Equal("32 Go", profile.TotalMemory);
        Assert.Equal(["GPU A", "GPU B"], profile.GraphicsAdapters);
    }
}

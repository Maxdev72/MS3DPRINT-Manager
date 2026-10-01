using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ThreeDFileSupportTests
{
    [Theory]
    [InlineData("piece.stl")]
    [InlineData("piece.OBJ")]
    public void IsPreviewable_AcceptsStlAndObj(string path)
    {
        Assert.True(ThreeDFileSupport.IsPreviewable(path));
    }

    [Theory]
    [InlineData("piece.step")]
    [InlineData("piece.3mf")]
    [InlineData("plan.pdf")]
    public void IsPreviewable_RejectsFormatsNotSupportedByTheBuiltInViewer(string path)
    {
        Assert.False(ThreeDFileSupport.IsPreviewable(path));
    }
}

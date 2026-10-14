using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ThreeDFileSupportTests
{
    [Theory]
    [InlineData("piece.stl")]
    [InlineData("piece.OBJ")]
    [InlineData("plateau.3mf")]
    [InlineData("plateau.3MF")]
    public void IsPreviewable_AcceptsStlObjAnd3mf(string path)
    {
        Assert.True(ThreeDFileSupport.IsPreviewable(path));
    }

    [Theory]
    [InlineData("piece.step")]
    [InlineData("plan.pdf")]
    public void IsPreviewable_RejectsFormatsNotSupportedByTheBuiltInViewer(string path)
    {
        Assert.False(ThreeDFileSupport.IsPreviewable(path));
    }
}

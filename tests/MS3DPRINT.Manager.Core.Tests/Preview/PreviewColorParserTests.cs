using MS3DPRINT.Manager.App.Preview;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class PreviewColorParserTests
{
    [Theory]
    [InlineData("#58A6FF", 0x58, 0xA6, 0xFF)]
    [InlineData("#ff6600", 0xFF, 0x66, 0x00)]
    public void AcceptsSixDigitHexColor(string text, byte red, byte green, byte blue)
    {
        Assert.True(PreviewColorParser.TryParse(text, out var color));
        Assert.Equal(red, color.R);
        Assert.Equal(green, color.G);
        Assert.Equal(blue, color.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#12345678")]
    [InlineData("red")]
    [InlineData("#ZZZZZZ")]
    public void RejectsOtherInputs(string text)
    {
        Assert.False(PreviewColorParser.TryParse(text, out _));
    }
}

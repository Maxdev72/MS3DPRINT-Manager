using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class PrintPageSelectionTests
{
    [Theory]
    [InlineData(null, 3, new[] { 0, 1, 2 })]
    [InlineData("", 3, new[] { 0, 1, 2 })]
    [InlineData("Toutes", 3, new[] { 0, 1, 2 })]
    [InlineData("2", 5, new[] { 1 })]
    [InlineData("1-3,5", 5, new[] { 0, 1, 2, 4 })]
    [InlineData(" 4 ; 2-3 ", 5, new[] { 3, 1, 2 })]
    [InlineData("2,2,1-2", 5, new[] { 1, 0 })]
    public void Parse_returns_zero_based_pages(string? text, int pageCount, int[] expected)
        => Assert.Equal(expected, PrintPageSelection.Parse(text, pageCount));

    [Theory]
    [InlineData("0", 3)]
    [InlineData("4", 3)]
    [InlineData("3-2", 5)]
    [InlineData("1-9", 5)]
    [InlineData("a", 5)]
    [InlineData("1-2-3", 5)]
    [InlineData(",", 5)]
    public void Parse_rejects_invalid_ranges(string text, int pageCount)
        => Assert.Throws<FormatException>(() => PrintPageSelection.Parse(text, pageCount));
}

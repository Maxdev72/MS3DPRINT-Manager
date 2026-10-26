using MS3DPRINT.Manager.Core.Recognition;

namespace MS3DPRINT.Manager.Core.Tests.Recognition;

public sealed class OcrLayoutTests
{
    [Fact]
    public void Columns_returned_as_separate_blocks_are_put_back_on_their_row_with_a_wide_gap_marked()
    {
        OcrWord[] words =
        [
            new("TOTAL", 10, 100, 60, 20), new("TTC", 80, 100, 40, 20), new("TVA", 10, 130, 40, 20),
            new("14,40", 400, 101, 60, 20), new("2,40", 410, 131, 50, 20)
        ];
        var lines = OcrLayout.ToText(words).Split('\n');
        Assert.Equal(["TOTAL TTC  14,40", "TVA  2,40"], lines);
    }

    [Fact]
    public void Tilted_rows_are_straightened_with_the_measured_angle()
    {
        // two rows slanted by about 5 degrees: the right-hand word sits 26 px lower than the left-hand one
        OcrWord[] words = [new("TOTAL", 10, 100, 60, 20), new("14,40", 310, 126, 60, 20), new("TVA", 10, 160, 40, 20), new("2,40", 310, 186, 50, 20)];
        Assert.Equal(2, OcrLayout.ToText(words, 5).Split('\n').Length);
        Assert.Equal(2, OcrLayout.ToText(words, -5).Split('\n').Length);
    }

    [Fact]
    public void No_words_give_no_text() => Assert.Equal("", OcrLayout.ToText([]));
}

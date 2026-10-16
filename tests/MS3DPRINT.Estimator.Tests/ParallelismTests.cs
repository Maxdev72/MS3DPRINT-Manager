namespace MS3DPRINT.Estimator.Tests;

public sealed class ParallelismTests
{
    [Theory]
    [InlineData(PerformanceMode.Economy, 28, 7)]
    [InlineData(PerformanceMode.Auto, 28, 14)]
    [InlineData(PerformanceMode.Maximum, 28, 28)]
    [InlineData(PerformanceMode.Economy, 4, 1)]
    [InlineData(PerformanceMode.Auto, 4, 2)]
    [InlineData(PerformanceMode.Auto, 2, 2)]
    [InlineData(PerformanceMode.Maximum, 1, 1)]
    [InlineData(PerformanceMode.Economy, 0, 1)]
    public void Each_mode_takes_its_share_of_the_threads(PerformanceMode mode, int processors, int expected)
        => Assert.Equal(expected, Parallelism.ThreadsFor(mode, processors));

    [Fact]
    public void Economy_never_takes_more_than_auto_and_auto_never_more_than_maximum()
    {
        foreach (var processors in new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 64 })
        {
            var economy = Parallelism.ThreadsFor(PerformanceMode.Economy, processors);
            var auto = Parallelism.ThreadsFor(PerformanceMode.Auto, processors);
            var maximum = Parallelism.ThreadsFor(PerformanceMode.Maximum, processors);
            Assert.True(economy <= auto && auto <= Math.Max(2, maximum), $"{processors} processors: {economy}/{auto}/{maximum}");
            Assert.True(economy >= 1);
        }
    }

    [Theory]
    [InlineData(PerformanceMode.Economy, 1)]
    [InlineData(PerformanceMode.Auto, 2)]
    [InlineData(PerformanceMode.Maximum, 4)]
    public void Thumbnails_are_drawn_a_few_at_a_time(PerformanceMode mode, int expected)
        => Assert.Equal(expected, Parallelism.ThumbnailSlotsFor(mode));
}

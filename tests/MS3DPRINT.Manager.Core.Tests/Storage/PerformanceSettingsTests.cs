using MS3DPRINT.Estimator;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class PerformanceSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-perf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Nothing_saved_means_automatic()
        => Assert.Equal(PerformanceMode.Auto, new PerformanceSettings(_directory).Load());

    [Theory]
    [InlineData(PerformanceMode.Economy)]
    [InlineData(PerformanceMode.Auto)]
    [InlineData(PerformanceMode.Maximum)]
    public void A_saved_mode_is_read_back(PerformanceMode mode)
    {
        var settings = new PerformanceSettings(_directory);
        settings.Save(mode);

        Assert.Equal(mode, new PerformanceSettings(_directory).Load());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Mode\":\"Turbo\"}")]
    [InlineData("{\"Mode\":\"42\"}")]
    [InlineData("{}")]
    public void An_unusable_file_means_automatic(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "performance.json"), content);

        Assert.Equal(PerformanceMode.Auto, new PerformanceSettings(_directory).Load());
    }

    [Fact]
    public void Every_mode_has_a_label_and_a_description()
    {
        foreach (var mode in Enum.GetValues<PerformanceMode>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PerformanceSettings.Label(mode)));
            Assert.False(string.IsNullOrWhiteSpace(PerformanceSettings.Description(mode)));
        }
    }
}

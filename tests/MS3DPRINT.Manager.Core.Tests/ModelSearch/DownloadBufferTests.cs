using System.Text.Json.Nodes;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Core.Tests.ModelSearch;

public sealed class DownloadBufferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-buffer-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData("piece.STL", true)]
    [InlineData("boite.3mf", true)]
    [InlineData("support.step", true)]
    [InlineData("support.stp", true)]
    [InlineData("scan.obj", true)]
    [InlineData("modele.amf", true)]
    [InlineData("archive.zip", false)]
    [InlineData("programme.exe", false)]
    [InlineData("piece.stl.crdownload", false)]
    [InlineData("sans-extension", false)]
    [InlineData("", false)]
    public void Only_slicer_formats_are_models(string name, bool expected) => Assert.Equal(expected, DownloadBuffer.IsModelFile(name));

    [Fact]
    public void PrepareProfile_points_the_browser_at_the_buffer_and_keeps_other_preferences()
    {
        var profile = Path.Combine(_root, "webview");
        var preferences = Path.Combine(profile, "EBWebView", "Default", "Preferences");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, """{"intl":{"selected_languages":"fr"},"download":{"extensions_to_open":"pdf"}}""");
        var buffer = new DownloadBuffer(Path.Combine(_root, "tampon"));

        buffer.PrepareProfile(profile);

        var json = JsonNode.Parse(File.ReadAllText(preferences))!;
        Assert.Equal(buffer.Folder, (string?)json["download"]!["default_directory"]);
        Assert.False((bool?)json["download"]!["prompt_for_download"]);
        Assert.Equal("pdf", (string?)json["download"]!["extensions_to_open"]);
        Assert.Equal("fr", (string?)json["intl"]!["selected_languages"]);
        Assert.True(Directory.Exists(buffer.Folder));
    }

    [Fact]
    public void PrepareProfile_creates_the_preferences_of_a_profile_that_never_ran()
    {
        var buffer = new DownloadBuffer(Path.Combine(_root, "tampon"));

        buffer.PrepareProfile(Path.Combine(_root, "neuf"));

        Assert.True(File.Exists(Path.Combine(_root, "neuf", "EBWebView", "Default", "Preferences")));
    }

    [Fact]
    public void PrepareProfile_replaces_unreadable_preferences_instead_of_failing()
    {
        var profile = Path.Combine(_root, "webview");
        var preferences = Path.Combine(profile, "EBWebView", "Default", "Preferences");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, "{ pas du json");

        new DownloadBuffer(Path.Combine(_root, "tampon")).PrepareProfile(profile);

        Assert.NotNull(JsonNode.Parse(File.ReadAllText(preferences)));
    }

    [Fact]
    public async Task A_model_is_announced_once_it_is_complete_and_not_while_it_downloads()
    {
        var folder = Path.Combine(_root, "tampon");
        using var buffer = new DownloadBuffer(folder);
        var ready = new TaskCompletionSource<BufferedFile>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        buffer.FileReady += file => { Interlocked.Increment(ref count); ready.TrySetResult(file); };
        buffer.StartWatching();

        // like the browser: a temporary name while it writes, the real name once it is done
        var temporary = Path.Combine(folder, "spatule.3mf.crdownload");
        await File.WriteAllBytesAsync(temporary, new byte[2048]);
        await Task.Delay(700);
        Assert.Equal(0, count);
        File.Move(temporary, Path.Combine(folder, "spatule.3mf"));

        var announced = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(800);

        Assert.Equal("spatule.3mf", announced.Name);
        Assert.Equal(2048, announced.Length);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Other_files_are_not_announced()
    {
        var folder = Path.Combine(_root, "tampon");
        using var buffer = new DownloadBuffer(folder);
        var count = 0;
        buffer.FileReady += _ => Interlocked.Increment(ref count);
        buffer.StartWatching();

        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "bonjour");
        await File.WriteAllBytesAsync(Path.Combine(folder, "archive.zip"), new byte[100]);
        await Task.Delay(1800);

        Assert.Equal(0, count);
    }

    [Fact]
    public void PurgeOlderThan_removes_only_old_files()
    {
        var folder = Path.Combine(_root, "tampon");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "ancien.stl");
        var recent = Path.Combine(folder, "recent.stl");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

        var removed = new DownloadBuffer(folder).PurgeOlderThan(TimeSpan.FromDays(7));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
